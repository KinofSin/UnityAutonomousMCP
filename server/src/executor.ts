import type { AgentPlan, AgentStep, UnityResponse } from "./types.js";
import type { UnityBridge } from "./unityBridge.js";
import { evaluateStep, resolveExecutionPolicyLimits } from "./policy.js";

export interface ExecutionOptions {
  allowDestructive?: boolean;
  stopOnError?: boolean;
}

export interface StepResult {
  step: AgentStep;
  skipped: boolean;
  success: boolean;
  reason?: string;
  response?: UnityResponse;
}

export interface ExecutionReport {
  goal: string;
  success: boolean;
  results: StepResult[];
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function readJobId(response: UnityResponse): string | undefined {
  if (!response.data || typeof response.data !== "object") {
    return undefined;
  }

  const record = response.data as Record<string, unknown>;
  const jobId = record.jobId;
  return typeof jobId === "string" && jobId.length > 0 ? jobId : undefined;
}

function readJobStatus(response: UnityResponse): string | undefined {
  if (!response.data || typeof response.data !== "object") {
    return undefined;
  }

  const data = response.data as Record<string, unknown>;
  const job = data.job;
  if (!job || typeof job !== "object") {
    return undefined;
  }

  const status = (job as Record<string, unknown>).status;
  return typeof status === "string" ? status.toLowerCase() : undefined;
}

export async function executePlan(
  plan: AgentPlan,
  bridge: UnityBridge,
  options: ExecutionOptions = {}
): Promise<ExecutionReport> {
  const allowDestructive = options.allowDestructive ?? false;
  const stopOnError = options.stopOnError ?? true;
  const limits = resolveExecutionPolicyLimits();
  const results: StepResult[] = [];

  for (const step of plan.steps) {
    const decision = evaluateStep(step, allowDestructive);
    if (!decision.allowed) {
      results.push({
        step,
        skipped: true,
        success: false,
        reason: decision.reason
      });

      if (stopOnError) {
        break;
      }

      continue;
    }

    const response = await bridge.call({
      tool: step.tool,
      params: step.params,
      requestId: step.id
    });

    const stepSuccess = response.success;
    results.push({
      step,
      skipped: false,
      success: stepSuccess,
      reason: stepSuccess ? undefined : response.error,
      response
    });

    if (!stepSuccess && stopOnError) {
      break;
    }

    if (stepSuccess && step.tool === "run_tests") {
      const jobId = readJobId(response);
      if (!jobId) {
        results.push({
          step: {
            id: `${step.id}-poll-setup`,
            action: "Poll test job",
            tool: "get_test_job",
            params: {},
            risk: "low"
          },
          skipped: false,
          success: false,
          reason: "run_tests did not return jobId",
          response: {
            success: false,
            error: "run_tests did not return jobId"
          }
        });

        if (stopOnError) {
          break;
        }

        continue;
      }

      const maxPollAttempts = limits.testPollAttempts;
      const pollIntervalMs = limits.testPollIntervalMs;
      const pollTimeoutMs = limits.testPollTimeoutMs;
      const pollStartedAt = Date.now();
      let terminal = false;

      for (let attempt = 1; attempt <= maxPollAttempts; attempt += 1) {
        if (Date.now() - pollStartedAt > pollTimeoutMs) {
          break;
        }

        if (attempt > 1) {
          await sleep(pollIntervalMs);
        }

        const pollStep: AgentStep = {
          id: `${step.id}-poll-${attempt}`,
          action: `Poll test job ${jobId} (attempt ${attempt})`,
          tool: "get_test_job",
          params: { jobId },
          risk: "low"
        };

        const pollResponse = await bridge.call({
          tool: "get_test_job",
          params: { jobId },
          requestId: pollStep.id
        });

        const status = readJobStatus(pollResponse);
        const terminalFailure = status === "failed";
        const pollSuccess = pollResponse.success && !terminalFailure;
        results.push({
          step: pollStep,
          skipped: false,
          success: pollSuccess,
          reason: pollSuccess ? undefined : pollResponse.error ?? (terminalFailure ? `Test job ${jobId} failed` : undefined),
          response: pollResponse
        });

        if (!pollSuccess) {
          if (stopOnError) {
            terminal = true;
            break;
          }
          continue;
        }

        if (status === "completed") {
          terminal = true;
          break;
        }

        if (status === "failed") {
          terminal = true;
          if (stopOnError) {
            break;
          }
        }
      }

      if (!terminal) {
        results.push({
          step: {
            id: `${step.id}-poll-timeout`,
            action: "Poll test job timeout",
            tool: "get_test_job",
            params: { jobId },
            risk: "low"
          },
          skipped: false,
          success: false,
          reason: `Timed out waiting for test job ${jobId}`,
          response: {
            success: false,
            error: `Timed out waiting for test job ${jobId} (attempts=${maxPollAttempts}, timeoutMs=${pollTimeoutMs})`
          }
        });

        if (stopOnError) {
          break;
        }
      }
    }
  }

  const success =
    results.length > 0 && results.every((result) => result.success || result.skipped);

  return {
    goal: plan.goal,
    success,
    results
  };
}
