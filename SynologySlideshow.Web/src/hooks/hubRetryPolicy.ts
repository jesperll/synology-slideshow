// Retry forever with a capped backoff (2s, 4s, 6s, ... up to 30s). SignalR's default
// policy gives up after ~4 attempts, which would leave a long-open page permanently
// disconnected after a longer outage or a redeploy. Mirrors the policy in
// useChannelConnection.ts.
export const hubRetryDelay = (previousRetryCount: number) => Math.min((previousRetryCount + 1) * 2000, 30000);

export const hubReconnectPolicy = {
  nextRetryDelayInMilliseconds: (retryContext: { previousRetryCount: number }) =>
    hubRetryDelay(retryContext.previousRetryCount)
};
