# Scheduler for Render Cron Jobs: wakes the API (which sleeps on the free plan) and
# runs the background jobs once. Retries while the API is cold-starting.
FROM alpine:3.20
RUN apk add --no-cache curl
CMD ["sh", "-c", "curl --fail --silent --show-error --retry 6 --retry-delay 20 --retry-all-errors --max-time 120 -X POST -H \"X-Job-Secret: ${JOB_SECRET}\" \"${API_URL}/api/jobs/run\""]
