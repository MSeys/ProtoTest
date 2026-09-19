import type { Deployment } from "./api";
import { hasBearerToken } from "./api";

export type LiveState = "connecting" | "live" | "polling";

export interface DeploymentSubscription {
  close(): void;
}

const SUBSCRIPTION = `subscription ConsoleDeploymentStatus {
  deploymentStatusChanged {
    id
    projectId
    environmentId
    version
    commitSha
    status
    requestedBy
    deployMinutes
    createdAtUtc
    completedAtUtc
  }
}`;

/**
 * The project screen follows the app's own GraphQL subscription when it can. A bearer session cannot
 * authenticate a browser WebSocket, and a socket can always drop, so the caller is told to fall back to
 * polling: `onState("polling")` and a visible refresh loop.
 */
export function subscribeToDeployments(
  onEvent: (deployment: Deployment) => void,
  onState: (state: LiveState) => void
): DeploymentSubscription {
  if (hasBearerToken()) {
    onState("polling");
    return { close: () => {} };
  }

  onState("connecting");
  const scheme = window.location.protocol === "https:" ? "wss" : "ws";
  let socket: WebSocket;
  let settled = false;
  try {
    socket = new WebSocket(`${scheme}://${window.location.host}/graphql`, "graphql-transport-ws");
  } catch {
    onState("polling");
    return { close: () => {} };
  }

  let ackTimer = 0;
  const fallback = () => {
    if (settled) {
      return;
    }
    settled = true;
    window.clearTimeout(ackTimer);
    try {
      socket.close();
    } catch {
      // Already closed.
    }
    onState("polling");
  };

  // A server that accepts the socket but never acknowledges would otherwise leave the screen silently
  // stale; after a few seconds polling takes over.
  ackTimer = window.setTimeout(fallback, 4000);

  socket.addEventListener("open", () => {
    socket.send(JSON.stringify({ type: "connection_init", payload: {} }));
  });

  socket.addEventListener("message", (event) => {
    let message: { id?: string; type?: string; payload?: { data?: { deploymentStatusChanged?: Deployment } } };
    try {
      message = JSON.parse(String(event.data));
    } catch {
      return;
    }

    switch (message.type) {
      case "connection_ack":
        window.clearTimeout(ackTimer);
        if (!settled) {
          onState("live");
        }
        socket.send(
          JSON.stringify({ id: "console", type: "subscribe", payload: { query: SUBSCRIPTION } })
        );
        break;
      case "next": {
        const deployment = message.payload?.data?.deploymentStatusChanged;
        if (deployment) {
          onEvent(deployment);
        }
        break;
      }
      case "ping":
        socket.send(JSON.stringify({ type: "pong", payload: message.payload ?? {} }));
        break;
      case "error":
      case "complete":
        fallback();
        break;
    }
  });

  socket.addEventListener("close", fallback);
  socket.addEventListener("error", fallback);

  return {
    close() {
      settled = true;
      window.clearTimeout(ackTimer);
      try {
        socket.close();
      } catch {
        // Already closed.
      }
    }
  };
}
