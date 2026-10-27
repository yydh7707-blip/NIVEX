export type DeviceUiPhase = "idle" | "verifying" | "verified" | "failed" | "creating" | "created";

export type DeviceUiEvent =
  | { type: "submit_verification" }
  | { type: "verification_ok" }
  | { type: "verification_fail" }
  | { type: "retry" }
  | { type: "submit_create" }
  | { type: "create_ok" }
  | { type: "create_fail"; consumed: boolean }
  | { type: "reset" };

export function reduceDeviceUi(phase: DeviceUiPhase, event: DeviceUiEvent): DeviceUiPhase {
  switch (phase) {
    case "idle":
      return event.type === "submit_verification" ? "verifying" : phase;
    case "verifying":
      if (event.type === "verification_ok") return "verified";
      if (event.type === "verification_fail") return "failed";
      return phase;
    case "verified":
      if (event.type === "submit_create") return "creating";
      if (event.type === "retry") return "idle";
      return phase;
    case "creating":
      if (event.type === "create_ok") return "created";
      if (event.type === "create_fail") return event.consumed ? "failed" : "verified";
      return phase;
    case "failed":
      return event.type === "retry" ? "idle" : phase;
    case "created":
      return event.type === "reset" ? "idle" : phase;
    default:
      return phase;
  }
}
