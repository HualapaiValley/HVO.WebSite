FROM wiremock/wiremock:3.13.2

COPY fake-ingest/telemetry.json /home/wiremock/mappings/telemetry.json
COPY fake-ingest/telemetry-unavailable.json /home/wiremock/mappings/telemetry-unavailable.json
COPY fake-ingest/set-unavailable.json /home/wiremock/mappings/set-unavailable.json
COPY fake-ingest/set-available.json /home/wiremock/mappings/set-available.json
