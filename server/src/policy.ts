import type { AgentStep } from "./types.js";

export interface PolicyDecision {
  allowed: boolean;
  reason?: string;
}

export interface ExecutionPolicyLimits {
  testPollAttempts: number;
  testPollIntervalMs: number;
  testPollTimeoutMs: number;
}

const BLOCKED_TOOLS = new Set<string>([
  "delete_scene",
  "assets-delete",
  "script-delete"
]);

const DEFAULT_TEST_POLL_ATTEMPTS = 30;
const DEFAULT_TEST_POLL_INTERVAL_MS = 1000;
const DEFAULT_TEST_POLL_TIMEOUT_MS = 120000;

function parsePositiveInteger(value: string | undefined, fallback: number): number {
  if (!value) {
    return fallback;
  }

  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    return fallback;
  }

  return Math.floor(parsed);
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(value, max));
}

export function resolveExecutionPolicyLimits(): ExecutionPolicyLimits {
  const attempts = clamp(
    parsePositiveInteger(process.env.UNITY_TEST_POLL_ATTEMPTS, DEFAULT_TEST_POLL_ATTEMPTS),
    1,
    300
  );
  const intervalMs = clamp(
    parsePositiveInteger(process.env.UNITY_TEST_POLL_INTERVAL_MS, DEFAULT_TEST_POLL_INTERVAL_MS),
    100,
    60000
  );
  const timeoutMs = clamp(
    parsePositiveInteger(process.env.UNITY_TEST_POLL_TIMEOUT_MS, DEFAULT_TEST_POLL_TIMEOUT_MS),
    1000,
    1800000
  );

  return {
    testPollAttempts: attempts,
    testPollIntervalMs: intervalMs,
    testPollTimeoutMs: timeoutMs
  };
}

export function evaluateStep(step: AgentStep, allowDestructive: boolean): PolicyDecision {
  if (BLOCKED_TOOLS.has(step.tool) && !allowDestructive) {
    return {
      allowed: false,
      reason: `Tool '${step.tool}' is blocked unless allowDestructive=true`
    };
  }

  if (step.risk === "high" && !allowDestructive) {
    return {
      allowed: false,
      reason: `High-risk step '${step.id}' requires allowDestructive=true`
    };
  }

  return { allowed: true };
}
