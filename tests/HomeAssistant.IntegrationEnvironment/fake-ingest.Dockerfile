FROM wiremock/wiremock:3.13.2@sha256:0d4ecb3e4dc8213fd7a4d37d6a78f6e6b553a6d2e15bd51b0999781282ac61b3

COPY fake-ingest/telemetry.json /home/wiremock/mappings/telemetry.json
COPY fake-ingest/telemetry-unavailable.json /home/wiremock/mappings/telemetry-unavailable.json
COPY fake-ingest/set-unavailable.json /home/wiremock/mappings/set-unavailable.json
COPY fake-ingest/set-available.json /home/wiremock/mappings/set-available.json
