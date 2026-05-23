#!/usr/bin/env bash
set -euo pipefail

COMPOSE_FILE="${COMPOSE_FILE:-deploy/observatory/docker-compose.rabbitmq.yml}"
SERVICE_NAME="${RABBITMQ_COMPOSE_SERVICE:-hvo-rabbitmq}"
VHOST="${RABBITMQ_VHOST:-/}"
SHOVEL_NAME="${RABBITMQ_SHOVEL_NAME:-hvo-weather-raw-v1-to-servicebus}"
SOURCE_QUEUE="${RABBITMQ_SOURCE_QUEUE:-hvo.weather.raw.v1}"
SOURCE_URI="${RABBITMQ_SHOVEL_SOURCE_URI:?Set RABBITMQ_SHOVEL_SOURCE_URI. Example: amqp://user:url-encoded-pass@localhost/%2f}"
DEST_URI="${SERVICEBUS_RABBITMQ_AMQP_URI:?Set SERVICEBUS_RABBITMQ_AMQP_URI from the Service Bus send-only policy AMQP connection string.}"
DEST_ADDRESS="${SERVICEBUS_DESTINATION_ADDRESS:-hvo-ingest}"

shovel_json=$(printf '{"src-protocol":"amqp091","src-uri":"%s","src-queue":"%s","dest-protocol":"amqp10","dest-uri":"%s","dest-address":"%s","ack-mode":"on-confirm","reconnect-delay":5}' \
  "${SOURCE_URI}" \
  "${SOURCE_QUEUE}" \
  "${DEST_URI}" \
  "${DEST_ADDRESS}")

docker compose -f "${COMPOSE_FILE}" exec -T "${SERVICE_NAME}" \
  rabbitmqctl -p "${VHOST}" set_parameter shovel "${SHOVEL_NAME}" "${shovel_json}" >/dev/null

echo "Configured RabbitMQ shovel ${SHOVEL_NAME} from ${SOURCE_QUEUE} to ${DEST_ADDRESS}."
echo "Use the RabbitMQ management UI or rabbitmqctl shovel_status on the host to inspect runtime status."
