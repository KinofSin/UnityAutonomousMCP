import { buildPlan } from "./planner.js";
import { executePlan } from "./executor.js";
import type { UnityBridge } from "./unityBridge.js";
import type { ExecutionReport } from "./executor.js";
import type { UnityRequest, UnityResponse } from "./types.js";

class FakeTestBridge implements UnityBridge {
  private readonly jobs = new Map<string, { polls: number; status: "running" | "completed" | "failed" }>();

  public constructor(private readonly outcome: "completed" | "failed") {}

  public async call(request: UnityRequest): Promise<UnityResponse> {
    if (request.tool === "run_tests") {
      const jobId = `smoke-job-${this.outcome}`;
      this.jobs.set(jobId, { polls: 0, status: "running" });
      return {
        success: true,
        data: {
          mode: (request.params.mode as string | undefined) ?? "editmode",
          status: "queued",
          jobId
        }
      };
    }

    if (request.tool === "get_test_job") {
      const jobId = (request.params.jobId as string | undefined) ?? "";
      const state = this.jobs.get(jobId);
      if (!state) {
        return { success: false, error: `Unknown test job '${jobId}'` };
      }

      state.polls += 1;
      if (state.polls >= 2) {
        state.status = this.outcome;
      }

      return {
        success: true,
        data: {
          job: {
            jobId,
            mode: "editmode",
            status: state.status,
            passed: state.status === "completed" ? 3 : 0,
            failed: state.status === "failed" ? 1 : 0,
            skipped: 0,
            totalTests: 3,
            completedTests: state.status === "completed" ? 3 : 1,
            tests: state.status === "completed"
              ? [
                  { name: "SmokeA", outcome: "passed", durationSeconds: 0.01 },
                  { name: "SmokeB", outcome: "passed", durationSeconds: 0.01 },
                  { name: "SmokeC", outcome: "passed", durationSeconds: 0.01 }
                ]
              : state.status === "failed"
                ? [{ name: "SmokeC", outcome: "failed", message: "Assertion failed" }]
              : []
          }
        }
      };
    }

    return {
      success: true,
      data: {
        echoedTool: request.tool,
        echoedParams: request.params
      }
    };
  }
}

async function runScenario(outcome: "completed" | "failed"): Promise<ExecutionReport> {
  const plan = buildPlan({
    goal: "Inspect scene, update script, and run tests",
    constraints: ["No destructive operations"],
    maxSteps: 10
  });

  const bridge = new FakeTestBridge(outcome);
  return executePlan(plan, bridge, {
    allowDestructive: false,
    stopOnError: true
  });
}

function assertScenario(report: ExecutionReport, expectedSuccess: boolean, label: string): number {
  const runTestsStep = report.results.find((result) => result.step.tool === "run_tests");
  const pollSteps = report.results.filter((result) => result.step.tool === "get_test_job");

  if (!runTestsStep || pollSteps.length === 0) {
    throw new Error(`${label} failed: expected run_tests and get_test_job polling results.`);
  }

  if (report.success !== expectedSuccess) {
    throw new Error(
      `${label} failed: expected success=${String(expectedSuccess)} but got ${String(report.success)}`
    );
  }

  return pollSteps.length;
}

async function main(): Promise<void> {
  const successReport = await runScenario("completed");
  const failedReport = await runScenario("failed");

  const successPollSteps = assertScenario(successReport, true, "Success smoke scenario");
  const failedPollSteps = assertScenario(failedReport, false, "Failure smoke scenario");

  process.stdout.write(
    `${JSON.stringify(
      {
        smoke: true,
        message: "Planner/executor smoke scenarios passed (success + failure).",
        successScenario: {
          steps: successReport.results.length,
          pollSteps: successPollSteps
        },
        failureScenario: {
          steps: failedReport.results.length,
          pollSteps: failedPollSteps
        }
      },
      null,
      2
    )}\n`
  );
}

main().catch((error: unknown) => {
  const message = error instanceof Error ? error.message : String(error);
  process.stderr.write(`Smoke test failed: ${message}\n`);
  process.exit(1);
});
