import json
import os
import socket
import time
import unicodedata
import uuid
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from flask import Flask, jsonify, request

app = Flask(__name__)

BRIDGE_VERSION = "3.5.0"
OPENCLAW_GATEWAY_URL = os.environ.get(
    "OPENCLAW_GATEWAY_URL", "http://127.0.0.1:18789"
).rstrip("/")
OPENCLAW_GATEWAY_TOKEN = (
    os.environ.get("OPENCLAW_GATEWAY_TOKEN")
    or os.environ.get("OPENCLAW_GATEWAY_PASSWORD")
    or ""
)
OPENCLAW_TIMEOUT_SECONDS = int(os.environ.get("OPENCLAW_TIMEOUT_SECONDS", "240"))
OPENCLAW_HEALTH_TIMEOUT_SECONDS = int(
    os.environ.get("OPENCLAW_HEALTH_TIMEOUT_SECONDS", "3")
)
OPENCLAW_MAX_OUTPUT_TOKENS = int(
    os.environ.get("OPENCLAW_MAX_OUTPUT_TOKENS", "600")
)
OPENCLAW_TEMPERATURE = float(os.environ.get("OPENCLAW_TEMPERATURE", "0"))
OPENCLAW_SYSTEM_INSTRUCTIONS = (
    "Return exactly one valid JSON object that follows the NPC decision contract "
    "in the selected agent workspace. Do not use Markdown or surrounding text. "
    "The agent must choose actorName, targetName, actionType, message, social "
    "deltas and a non-empty contextual reason. The bridge derives interactionType "
    "and animationTrigger from actionType, so these technical fields may be "
    "omitted. reason must contain one short sentence even when actionType is Idle. "
    "Use concise European Portuguese and never use voce or voces. Do not invent "
    "targets, memories or events."
)


def env_bool(name, default):
    raw_value = os.environ.get(name)
    if raw_value is None:
        return default
    return raw_value.strip().lower() in {"1", "true", "yes", "on"}


OPENCLAW_REPAIR_ENABLED = env_bool("OPENCLAW_REPAIR_ENABLED", True)
OPENCLAW_FALLBACK_ENABLED = env_bool("OPENCLAW_FALLBACK_ENABLED", True)
OPENCLAW_STRICT_SCHEMA = env_bool("OPENCLAW_STRICT_SCHEMA", True)
BRIDGE_DEBUG = env_bool("OPENCLAW_BRIDGE_DEBUG", False)

AGENT_ROUTES = {
    "lia": os.environ.get("OPENCLAW_AGENT_LIA", "openclaw/lia"),
    "afonso": os.environ.get("OPENCLAW_AGENT_AFONSO", "openclaw/afonso"),
    "marta": os.environ.get("OPENCLAW_AGENT_MARTA", "openclaw/marta"),
    "duarte": os.environ.get("OPENCLAW_AGENT_DUARTE", "openclaw/duarte"),
}

SOCIETY_SCHEDULER_ROUTE = os.environ.get(
    "OPENCLAW_SOCIETY_SCHEDULER_AGENT", "openclaw/society"
)
SOCIETY_SCHEDULER_MAX_OUTPUT_TOKENS = int(
    os.environ.get("OPENCLAW_SOCIETY_SCHEDULER_MAX_OUTPUT_TOKENS", "220")
)
SOCIETY_SCHEDULER_SYSTEM_INSTRUCTIONS = (
    "Return exactly one valid JSON object for the society scheduler contract. "
    "The object must contain only actorName, priority and reason. "
    "Choose exactly one actorName from eligibleActors. Do not choose the final "
    "action, target, message or social deltas. Prefer social diversity: avoid "
    "selecting the same actor repeatedly when other eligible actors also have "
    "useful goals, relationships or recent context. Consider role, goal, recent "
    "memories, relationships and recent social events. Use concise European "
    "Portuguese for reason. Do not use Markdown or surrounding text."
)

ALLOWED_ACTIONS = {
    "Idle",
    "Talk",
    "AskForHelp",
    "Collaborate",
    "PositiveRelation",
    "Conflict",
    "ShareMemory",
}
ALLOWED_ANIMATIONS = {"Idle", "Talk", "Work"}
EXPECTED_ANIMATION_BY_ACTION = {
    "Idle": "Idle",
    "Talk": "Talk",
    "AskForHelp": "Talk",
    "Collaborate": "Work",
    "PositiveRelation": "Talk",
    "Conflict": "Talk",
    "ShareMemory": "Talk",
}
REQUIRED_BEHAVIOURAL_FIELDS = {
    "actorName",
    "targetName",
    "actionType",
    "message",
    "relationshipDelta",
    "actorDelta",
    "targetDelta",
    "reason",
}
TECHNICAL_DECISION_FIELDS = {
    "interactionType",
    "animationTrigger",
}
ALLOWED_DECISION_FIELDS = REQUIRED_BEHAVIOURAL_FIELDS | TECHNICAL_DECISION_FIELDS


def normalize_text(value):
    if value is None:
        return ""
    normalized = unicodedata.normalize("NFKD", str(value))
    return normalized.encode("ascii", "ignore").decode("ascii").lower().strip()


def safe_text(data, key, default=""):
    if not isinstance(data, dict):
        return default
    value = data.get(key, default)
    return default if value is None else str(value).strip()


def safe_list(data, key):
    if not isinstance(data, dict):
        return []
    value = data.get(key, [])
    return value if isinstance(value, list) else []


def trim_text(value, maximum_length):
    text = str(value or "").strip()
    return text if len(text) <= maximum_length else text[:maximum_length].rstrip()


def is_candidate_available(candidate):
    if not isinstance(candidate, dict):
        return False
    return not (
        candidate.get("isBusy") is True
        or candidate.get("busy") is True
        or candidate.get("isAvailable") is False
    )


def get_available_target_names(payload):
    actor_name = normalize_text(safe_text(payload.get("actor", {}), "name", ""))
    available_targets = []
    for npc in safe_list(payload.get("society", {}), "npcs"):
        npc_name = safe_text(npc, "name", "")
        if not npc_name or normalize_text(npc_name) == actor_name:
            continue
        if is_candidate_available(npc):
            available_targets.append(npc_name)
    return available_targets


def resolve_agent_route(actor_name):
    agent_route = AGENT_ROUTES.get(normalize_text(actor_name))
    if not agent_route:
        raise ValueError(f"No OpenClaw agent route is configured for actor '{actor_name}'.")
    return agent_route


def build_gateway_headers():
    headers = {
        "Accept": "application/json",
        "Content-Type": "application/json; charset=utf-8",
    }
    if OPENCLAW_GATEWAY_TOKEN:
        headers["Authorization"] = f"Bearer {OPENCLAW_GATEWAY_TOKEN}"
    return headers


def gateway_request_json(path, method="GET", payload=None, timeout=None):
    timeout = OPENCLAW_TIMEOUT_SECONDS if timeout is None else timeout
    url = f"{OPENCLAW_GATEWAY_URL}{path}"
    body = None
    if payload is not None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")

    http_request = Request(
        url=url,
        data=body,
        headers=build_gateway_headers(),
        method=method,
    )

    try:
        with urlopen(http_request, timeout=timeout) as response:
            response_text = response.read().decode("utf-8", errors="replace")
    except HTTPError as exception:
        error_body = exception.read().decode("utf-8", errors="replace")
        raise RuntimeError(
            f"OpenClaw Gateway returned HTTP {exception.code}: {error_body}"
        ) from exception
    except (URLError, socket.timeout, TimeoutError) as exception:
        raise RuntimeError(
            f"Could not reach OpenClaw Gateway at {url}: {exception}"
        ) from exception

    if not response_text.strip():
        return {}

    try:
        parsed_response = json.loads(response_text)
    except json.JSONDecodeError as exception:
        raise RuntimeError("OpenClaw Gateway returned invalid JSON.") from exception

    if not isinstance(parsed_response, dict):
        raise RuntimeError("OpenClaw Gateway response must be a JSON object.")
    return parsed_response


def check_gateway_available():
    try:
        gateway_request_json(
            "/v1/models",
            method="GET",
            timeout=OPENCLAW_HEALTH_TIMEOUT_SECONDS,
        )
        return True, ""
    except Exception as exception:
        return False, trim_text(str(exception), 500)


def extract_openclaw_output_text(openclaw_response):
    output_text_parts = []
    for output_item in openclaw_response.get("output", []):
        if not isinstance(output_item, dict):
            continue
        for content_item in output_item.get("content", []):
            if not isinstance(content_item, dict):
                continue
            if content_item.get("type") != "output_text":
                continue
            text = content_item.get("text", "")
            if isinstance(text, str) and text.strip():
                output_text_parts.append(text.strip())

    if not output_text_parts:
        raise ValueError("OpenClaw response did not contain output_text.")
    return "\n".join(output_text_parts)


def call_openclaw_agent(agent_route, agent_input):
    gateway_payload = {
        "model": agent_route,
        "instructions": OPENCLAW_SYSTEM_INSTRUCTIONS,
        "input": agent_input,
        "stream": False,
        "max_output_tokens": OPENCLAW_MAX_OUTPUT_TOKENS,
        "temperature": OPENCLAW_TEMPERATURE,
    }
    openclaw_response = gateway_request_json(
        "/v1/responses",
        method="POST",
        payload=gateway_payload,
    )
    response_status = openclaw_response.get("status")
    if response_status != "completed":
        raise RuntimeError(
            "OpenClaw agent run did not complete successfully. "
            f"Status: {response_status}"
        )

    metadata = {
        "responseId": openclaw_response.get("id", ""),
        "model": openclaw_response.get("model", agent_route),
        "usage": openclaw_response.get("usage", {}),
    }
    return extract_openclaw_output_text(openclaw_response), metadata


def remove_code_fences(text):
    cleaned = str(text or "").strip()
    if not cleaned.startswith("```"):
        return cleaned
    lines = cleaned.splitlines()
    if lines and lines[0].strip().startswith("```"):
        lines = lines[1:]
    if lines and lines[-1].strip() == "```":
        lines = lines[:-1]
    return "\n".join(lines).strip()


def extract_balanced_json_candidates(text):
    source = str(text or "")
    candidates = []
    for start_index, character in enumerate(source):
        if character != "{":
            continue
        depth = 0
        in_string = False
        escaping = False
        for current_index in range(start_index, len(source)):
            current_character = source[current_index]
            if escaping:
                escaping = False
                continue
            if current_character == "\\":
                escaping = True
                continue
            if current_character == '"':
                in_string = not in_string
                continue
            if in_string:
                continue
            if current_character == "{":
                depth += 1
            elif current_character == "}":
                depth -= 1
                if depth == 0:
                    candidates.append(source[start_index : current_index + 1])
                    break
    return candidates


def extract_json_object(text):
    cleaned = remove_code_fences(text)
    try:
        parsed = json.loads(cleaned)
        if isinstance(parsed, dict):
            return parsed
    except json.JSONDecodeError:
        pass

    for candidate in extract_balanced_json_candidates(cleaned):
        try:
            parsed = json.loads(candidate)
            if isinstance(parsed, dict):
                return parsed
        except json.JSONDecodeError:
            continue
    raise ValueError("No valid JSON object was found in agent output.")


def build_agent_input(payload):
    society = payload.get("society", {})
    context = {
        "requestType": "npc_autonomous_decision",
        "actor": payload.get("actor", {}),
        "availableTargets": get_available_target_names(payload),
        "society": {
            "npcs": safe_list(society, "npcs"),
            "relationships": safe_list(society, "relationships")[-12:],
            "recentSocialEvents": safe_list(society, "recentSocialEvents")[-8:],
        },
        "instruction": (
            "Decide the actor's next autonomous action now. Follow the exact "
            "decision contract defined in the agent workspace. Always provide "
            "a non-empty reason, including for Idle. The bridge derives "
            "interactionType and animationTrigger from actionType."
        ),
    }
    return json.dumps(context, ensure_ascii=False, separators=(",", ":"))


def build_repair_input(payload, raw_agent_output, validation_error):
    society = payload.get("society", {})
    repair_context = {
        "requestType": "npc_autonomous_decision_repair",
        "actor": payload.get("actor", {}),
        "availableTargets": get_available_target_names(payload),
        "relationships": safe_list(society, "relationships"),
        "recentSocialEvents": safe_list(society, "recentSocialEvents"),
        "validationError": trim_text(validation_error, 1000),
        "invalidOutput": trim_text(raw_agent_output, 4000),
        "instruction": (
            "Correct the invalid decision without changing its behavioural intent "
            "unless required by the validation error. Return exactly one valid "
            "JSON object. reason must be non-empty and grounded in the supplied "
            "context. Use European Portuguese."
        ),
    }
    return json.dumps(repair_context, ensure_ascii=False, separators=(",", ":"))


def require_string(data, key):
    value = data.get(key)
    if not isinstance(value, str):
        raise ValueError(f"Field '{key}' must be a string.")
    return value.strip()


def require_integer(value, path, minimum, maximum):
    if isinstance(value, bool) or not isinstance(value, int):
        raise ValueError(f"Field '{path}' must be an integer.")
    if value < minimum or value > maximum:
        raise ValueError(f"Field '{path}' must be between {minimum} and {maximum}.")
    return value


def validate_relationship_delta(value):
    if not isinstance(value, dict):
        raise ValueError("Field 'relationshipDelta' must be an object.")
    required_keys = {"trust", "affinity", "tension"}
    if set(value.keys()) != required_keys:
        raise ValueError(
            "relationshipDelta must contain exactly trust, affinity and tension."
        )
    return {
        "trust": require_integer(value["trust"], "relationshipDelta.trust", -10, 10),
        "affinity": require_integer(
            value["affinity"], "relationshipDelta.affinity", -10, 10
        ),
        "tension": require_integer(
            value["tension"], "relationshipDelta.tension", -10, 15
        ),
    }


def validate_agent_delta(value, field_name):
    if not isinstance(value, dict):
        raise ValueError(f"Field '{field_name}' must be an object.")
    required_keys = {"mood", "trust"}
    if set(value.keys()) != required_keys:
        raise ValueError(f"{field_name} must contain exactly mood and trust.")
    return {
        "mood": require_integer(value["mood"], f"{field_name}.mood", -10, 10),
        "trust": require_integer(value["trust"], f"{field_name}.trust", -10, 10),
    }


def validate_decision(payload, agent_decision):
    if not isinstance(agent_decision, dict):
        raise ValueError("Agent decision must be a JSON object.")

    decision_fields = set(agent_decision.keys())
    missing_fields = REQUIRED_BEHAVIOURAL_FIELDS - decision_fields
    if missing_fields:
        raise ValueError(
            "Agent decision is missing fields: " + ", ".join(sorted(missing_fields))
        )

    if OPENCLAW_STRICT_SCHEMA:
        unexpected_fields = decision_fields - ALLOWED_DECISION_FIELDS
        if unexpected_fields:
            raise ValueError(
                "Agent decision contains unexpected fields: "
                + ", ".join(sorted(unexpected_fields))
            )

    actor_name = safe_text(payload.get("actor", {}), "name", "")
    if not actor_name:
        raise ValueError("Payload actor must have a name.")

    decision_actor_name = require_string(agent_decision, "actorName")
    if decision_actor_name != actor_name:
        raise ValueError(f"actorName must be exactly '{actor_name}'.")

    action_type = require_string(agent_decision, "actionType")
    if action_type not in ALLOWED_ACTIONS:
        raise ValueError(f"Invalid actionType '{action_type}'.")

    target_name = require_string(agent_decision, "targetName")
    available_targets = get_available_target_names(payload)
    if action_type == "Idle":
        if target_name:
            raise ValueError("Idle decisions must use an empty targetName.")
    elif target_name not in available_targets:
        raise ValueError(
            f"Invalid targetName '{target_name}'. Available targets: {available_targets}"
        )

    message = trim_text(require_string(agent_decision, "message"), 300)
    if action_type != "Idle" and not message:
        raise ValueError("Non-Idle decisions require a message.")

    reason = trim_text(require_string(agent_decision, "reason"), 500)
    if not reason:
        raise ValueError("Decision reason cannot be empty.")

    relationship_delta = validate_relationship_delta(
        agent_decision["relationshipDelta"]
    )
    actor_delta = validate_agent_delta(agent_decision["actorDelta"], "actorDelta")
    target_delta = validate_agent_delta(
        agent_decision["targetDelta"], "targetDelta"
    )

    if action_type == "Idle":
        all_idle_values = [
            *relationship_delta.values(),
            *actor_delta.values(),
            *target_delta.values(),
        ]
        if any(value != 0 for value in all_idle_values):
            raise ValueError("Idle decisions must use zero for all social deltas.")

    normalizations_applied = []

    provided_interaction_type = agent_decision.get("interactionType")
    if (
        provided_interaction_type is not None
        and provided_interaction_type != action_type
    ):
        normalizations_applied.append("interactionType")
    interaction_type = action_type

    expected_animation = EXPECTED_ANIMATION_BY_ACTION[action_type]
    provided_animation_trigger = agent_decision.get("animationTrigger")
    if (
        provided_animation_trigger is not None
        and provided_animation_trigger != expected_animation
    ):
        normalizations_applied.append("animationTrigger")
    animation_trigger = expected_animation

    validated_decision = {
        "actorName": actor_name,
        "targetName": target_name,
        "actionType": action_type,
        "interactionType": interaction_type,
        "message": message,
        "animationTrigger": animation_trigger,
        "relationshipDelta": relationship_delta,
        "actorDelta": actor_delta,
        "targetDelta": target_delta,
        "reason": reason,
    }
    return validated_decision, normalizations_applied


def build_success_decision(
    validated_decision,
    agent_route,
    openclaw_metadata,
    raw_model_text,
    repair_used,
    normalizations_applied,
):
    decision = {
        "requestId": str(uuid.uuid4()),
        "decisionSource": "openclaw_agent",
        "serverTimestamp": int(time.time()),
        "agentRoute": agent_route,
        "openclawResponseId": openclaw_metadata.get("responseId", ""),
        "openclawModel": openclaw_metadata.get("model", agent_route),
        "llmRepairUsed": repair_used,
        "technicalNormalizationApplied": bool(normalizations_applied),
        "technicalNormalizations": normalizations_applied,
        "rawModelTextLength": len(raw_model_text),
        "usage": openclaw_metadata.get("usage", {}),
    }
    decision.update(validated_decision)
    return decision


def build_idle_fallback_decision(payload, reason, agent_route=""):
    actor_name = safe_text(payload.get("actor", {}), "name", "NPC")
    return {
        "requestId": str(uuid.uuid4()),
        "decisionSource": "local_idle_fallback",
        "serverTimestamp": int(time.time()),
        "agentRoute": agent_route,
        "actorName": actor_name,
        "targetName": "",
        "actionType": "Idle",
        "interactionType": "Idle",
        "message": f"{actor_name} decidiu aguardar antes de agir.",
        "animationTrigger": "Idle",
        "relationshipDelta": {"trust": 0, "affinity": 0, "tension": 0},
        "actorDelta": {"mood": 0, "trust": 0},
        "targetDelta": {"mood": 0, "trust": 0},
        "reason": (
            "Fallback técnico seguro: o NPC permanece inativo porque não foi "
            "possível obter uma decisão válida do OpenClaw."
        ),
        "fallbackReason": trim_text(reason, 1500),
    }


def build_next_action_decision(payload):
    actor_name = safe_text(payload.get("actor", {}), "name", "")
    agent_route = ""
    first_agent_text = ""
    first_error = None

    try:
        agent_route = resolve_agent_route(actor_name)
        first_agent_text, first_metadata = call_openclaw_agent(
            agent_route, build_agent_input(payload)
        )
        first_agent_decision = extract_json_object(first_agent_text)
        validated_decision, normalizations = validate_decision(
            payload, first_agent_decision
        )
        return build_success_decision(
            validated_decision,
            agent_route,
            first_metadata,
            first_agent_text,
            repair_used=False,
            normalizations_applied=normalizations,
        )
    except Exception as exception:
        first_error = exception
        app.logger.warning(
            "OpenClaw decision failed for actor=%s route=%s error=%s",
            actor_name,
            agent_route,
            exception,
        )

    if OPENCLAW_REPAIR_ENABLED and agent_route and first_agent_text:
        try:
            repaired_text, repaired_metadata = call_openclaw_agent(
                agent_route,
                build_repair_input(payload, first_agent_text, str(first_error)),
            )
            repaired_decision = extract_json_object(repaired_text)
            validated_repaired, normalizations = validate_decision(
                payload, repaired_decision
            )
            result = build_success_decision(
                validated_repaired,
                agent_route,
                repaired_metadata,
                repaired_text,
                repair_used=True,
                normalizations_applied=normalizations,
            )
            result["firstAgentError"] = trim_text(str(first_error), 1000)
            return result
        except Exception as repair_exception:
            combined_error = (
                f"First attempt failed: {first_error} | "
                f"Repair attempt failed: {repair_exception}"
            )
            app.logger.warning(
                "OpenClaw repair failed for actor=%s route=%s error=%s",
                actor_name,
                agent_route,
                repair_exception,
            )
    else:
        combined_error = str(first_error)

    if OPENCLAW_FALLBACK_ENABLED:
        return build_idle_fallback_decision(payload, combined_error, agent_route)
    raise RuntimeError(combined_error)


def normalize_interaction_payload(payload):
    if isinstance(payload.get("society"), dict):
        return payload

    actor = payload.get("actor", {})
    target = payload.get("target", {})
    relationship = payload.get("relationship", {})
    target_name = safe_text(
        target,
        "name",
        safe_text(relationship, "targetName", ""),
    )
    normalized_relationship = relationship
    if (
        isinstance(relationship, dict)
        and target_name
        and "targetName" not in relationship
    ):
        normalized_relationship = dict(relationship)
        normalized_relationship["targetName"] = target_name

    return {
        "actor": actor,
        "society": {
            "npcs": [target] if isinstance(target, dict) and target else [],
            "relationships": (
                [normalized_relationship]
                if isinstance(normalized_relationship, dict) and normalized_relationship
                else []
            ),
            "recentSocialEvents": [],
        },
    }


def validate_payload_structure(payload):
    actor = payload.get("actor")
    society = payload.get("society")
    if not isinstance(actor, dict):
        return "Payload must include an actor object."
    if not safe_text(actor, "name", ""):
        return "Payload actor must include a name."
    if not isinstance(society, dict):
        return "Payload must include a society object."
    if not isinstance(society.get("npcs", []), list):
        return "society.npcs must be an array."
    if not isinstance(society.get("relationships", []), list):
        return "society.relationships must be an array."
    if not isinstance(society.get("recentSocialEvents", []), list):
        return "society.recentSocialEvents must be an array."
    return ""


def process_decision_payload(payload):
    validation_error = validate_payload_structure(payload)
    if validation_error:
        return jsonify({"error": validation_error}), 400
    try:
        return jsonify(build_next_action_decision(payload))
    except Exception as exception:
        return (
            jsonify(
                {
                    "error": "OpenClaw decision request failed.",
                    "details": trim_text(str(exception), 1500),
                }
            ),
            502,
        )




def get_scheduler_candidates(payload):
    candidates = []
    raw_candidates = payload.get("eligibleActors", [])

    if not isinstance(raw_candidates, list) or not raw_candidates:
        society = payload.get("society", {})
        raw_candidates = safe_list(society, "npcs")

    for candidate in raw_candidates:
        if not isinstance(candidate, dict):
            continue
        name = safe_text(candidate, "name", "")
        if not name:
            continue
        if not is_candidate_available(candidate):
            continue
        candidates.append(candidate)

    return candidates


def build_scheduler_agent_input(payload):
    candidates = get_scheduler_candidates(payload)
    society = payload.get("society", {})
    context = {
        "requestType": "society_actor_selection",
        "eligibleActors": candidates,
        "society": {
            "relationships": safe_list(society, "relationships")[-12:],
            "recentSocialEvents": safe_list(society, "recentSocialEvents")[-8:],
        },
        "instruction": (
            "Choose which eligible NPC should act next. Return only actorName, "
            "priority and reason. The selected NPC will later decide its own "
            "action through its personal OpenClaw agent."
        ),
    }
    return json.dumps(context, ensure_ascii=False, separators=(",", ":"))


def call_society_scheduler_agent(agent_input):
    gateway_payload = {
        "model": SOCIETY_SCHEDULER_ROUTE,
        "instructions": SOCIETY_SCHEDULER_SYSTEM_INSTRUCTIONS,
        "input": agent_input,
        "stream": False,
        "max_output_tokens": SOCIETY_SCHEDULER_MAX_OUTPUT_TOKENS,
        "temperature": OPENCLAW_TEMPERATURE,
    }
    openclaw_response = gateway_request_json(
        "/v1/responses",
        method="POST",
        payload=gateway_payload,
    )
    response_status = openclaw_response.get("status")
    if response_status != "completed":
        raise RuntimeError(
            "OpenClaw society scheduler did not complete successfully. "
            f"Status: {response_status}"
        )

    metadata = {
        "responseId": openclaw_response.get("id", ""),
        "model": openclaw_response.get("model", SOCIETY_SCHEDULER_ROUTE),
        "usage": openclaw_response.get("usage", {}),
    }
    return extract_openclaw_output_text(openclaw_response), metadata


def validate_scheduler_payload(payload):
    candidates = get_scheduler_candidates(payload)
    if not candidates:
        return "Payload must include at least one eligible actor."

    society = payload.get("society", {})
    if society is not None and not isinstance(society, dict):
        return "Payload society must be an object when provided."

    return ""


def validate_scheduler_decision(payload, scheduler_decision):
    if not isinstance(scheduler_decision, dict):
        raise ValueError("Scheduler decision must be a JSON object.")

    allowed_fields = {"actorName", "priority", "reason"}
    decision_fields = set(scheduler_decision.keys())

    missing_fields = allowed_fields - decision_fields
    if missing_fields:
        raise ValueError(
            "Scheduler decision is missing fields: "
            + ", ".join(sorted(missing_fields))
        )

    if OPENCLAW_STRICT_SCHEMA:
        unexpected_fields = decision_fields - allowed_fields
        if unexpected_fields:
            raise ValueError(
                "Scheduler decision contains unexpected fields: "
                + ", ".join(sorted(unexpected_fields))
            )

    candidates = get_scheduler_candidates(payload)
    candidate_names = [safe_text(candidate, "name", "") for candidate in candidates]

    actor_name = require_string(scheduler_decision, "actorName")
    if actor_name not in candidate_names:
        raise ValueError(
            f"Invalid actorName '{actor_name}'. Eligible actors: {candidate_names}"
        )

    priority_raw = scheduler_decision.get("priority")
    try:
        priority = int(priority_raw)
    except (TypeError, ValueError):
        priority = 0
    priority = max(0, min(10, priority))

    reason = trim_text(require_string(scheduler_decision, "reason"), 500)
    if not reason:
        raise ValueError("Scheduler reason cannot be empty.")

    return {
        "actorName": actor_name,
        "priority": priority,
        "reason": reason,
    }


def build_scheduler_success(validated_decision, metadata, raw_model_text):
    return {
        "requestId": str(uuid.uuid4()),
        "decisionSource": "openclaw_society_scheduler",
        "serverTimestamp": int(time.time()),
        "schedulerRoute": SOCIETY_SCHEDULER_ROUTE,
        "openclawResponseId": metadata.get("responseId", ""),
        "openclawModel": metadata.get("model", SOCIETY_SCHEDULER_ROUTE),
        "rawModelTextLength": len(raw_model_text),
        "usage": metadata.get("usage", {}),
        "actorName": validated_decision["actorName"],
        "priority": validated_decision["priority"],
        "reason": validated_decision["reason"],
    }


def build_scheduler_fallback(payload, reason):
    candidates = get_scheduler_candidates(payload)
    actor_name = safe_text(candidates[0], "name", "") if candidates else ""
    return {
        "requestId": str(uuid.uuid4()),
        "decisionSource": "local_round_robin_fallback",
        "serverTimestamp": int(time.time()),
        "schedulerRoute": SOCIETY_SCHEDULER_ROUTE,
        "actorName": actor_name,
        "priority": 0,
        "reason": (
            "Fallback técnico seguro: o scheduler social falhou, por isso "
            "deve ser usada a ordem local do manager."
        ),
        "fallbackReason": trim_text(reason, 1500),
    }


def build_scheduler_choice(payload):
    try:
        raw_text, metadata = call_society_scheduler_agent(
            build_scheduler_agent_input(payload)
        )
        scheduler_decision = extract_json_object(raw_text)
        validated_decision = validate_scheduler_decision(payload, scheduler_decision)
        return build_scheduler_success(validated_decision, metadata, raw_text)
    except Exception as exception:
        app.logger.warning(
            "OpenClaw society scheduler failed route=%s error=%s",
            SOCIETY_SCHEDULER_ROUTE,
            exception,
        )
        if OPENCLAW_FALLBACK_ENABLED:
            return build_scheduler_fallback(payload, str(exception))
        raise


def process_scheduler_payload(payload):
    validation_error = validate_scheduler_payload(payload)
    if validation_error:
        return jsonify({"error": validation_error}), 400

    try:
        return jsonify(build_scheduler_choice(payload))
    except Exception as exception:
        return (
            jsonify(
                {
                    "error": "OpenClaw society scheduler request failed.",
                    "details": trim_text(str(exception), 1500),
                }
            ),
            502,
        )

@app.get("/health")
def health():
    gateway_available, gateway_error = check_gateway_available()
    return jsonify(
        {
            "status": "ok" if gateway_available else "degraded",
            "service": "OpenClaw Unity Bridge",
            "version": BRIDGE_VERSION,
            "gatewayUrl": OPENCLAW_GATEWAY_URL,
            "gatewayAvailable": gateway_available,
            "gatewayError": gateway_error,
            "decisionPath": "OpenClaw Gateway /v1/responses",
            "configuredAgentRoutes": AGENT_ROUTES,
            "configuredSocietySchedulerRoute": SOCIETY_SCHEDULER_ROUTE,
            "repairEnabled": OPENCLAW_REPAIR_ENABLED,
            "fallbackEnabled": OPENCLAW_FALLBACK_ENABLED,
            "fallbackAction": "Idle",
            "strictSchema": OPENCLAW_STRICT_SCHEMA,
            "temperature": OPENCLAW_TEMPERATURE,
            "technicalFieldsDerived": [
                "interactionType",
                "animationTrigger",
            ],
        }
    )


@app.post("/choose_next_actor")
def choose_next_actor():
    payload = request.get_json(silent=True)
    if payload is None:
        return jsonify({"error": "Request body must be valid JSON."}), 400
    return process_scheduler_payload(payload)


@app.post("/decide_next_action")
def decide_next_action():
    payload = request.get_json(silent=True)
    if payload is None:
        return jsonify({"error": "Request body must be valid JSON."}), 400
    return process_decision_payload(payload)


@app.post("/decide_interaction")
def decide_interaction():
    payload = request.get_json(silent=True)
    if payload is None:
        return jsonify({"error": "Request body must be valid JSON."}), 400
    return process_decision_payload(normalize_interaction_payload(payload))


@app.post("/decide")
def decide():
    payload = request.get_json(silent=True)
    if payload is None:
        return jsonify({"error": "Request body must be valid JSON."}), 400
    return process_decision_payload(normalize_interaction_payload(payload))


if __name__ == "__main__":
    port = int(os.environ.get("OPENCLAW_PORT", "5055"))
    app.run(
        host="127.0.0.1",
        port=port,
        debug=BRIDGE_DEBUG,
        threaded=True,
    )
