#!/usr/bin/env bash
set -euo pipefail

COMPOSE_FILE="${COMPOSE_FILE:-deploy/observatory/docker-compose.rabbitmq.yml}"
SERVICE_NAME="${RABBITMQ_COMPOSE_SERVICE:-hvo-rabbitmq}"
VHOST="${RABBITMQ_VHOST:-/}"
RABBITMQ_USER="${RABBITMQ_DEFAULT_USER:?Set RABBITMQ_DEFAULT_USER}"
RABBITMQ_PASS="${RABBITMQ_DEFAULT_PASS:?Set RABBITMQ_DEFAULT_PASS}"

if docker compose -f "${COMPOSE_FILE}" exec -T "${SERVICE_NAME}" \
  rabbitmqctl authenticate_user "${RABBITMQ_USER}" "${RABBITMQ_PASS}" >/dev/null 2>&1; then
  echo "RabbitMQ user ${RABBITMQ_USER} already authenticates."
else
  docker compose -f "${COMPOSE_FILE}" exec -T "${SERVICE_NAME}" \
    rabbitmqctl add_user "${RABBITMQ_USER}" "${RABBITMQ_PASS}" >/dev/null 2>&1 || \
  docker compose -f "${COMPOSE_FILE}" exec -T "${SERVICE_NAME}" \
    rabbitmqctl change_password "${RABBITMQ_USER}" "${RABBITMQ_PASS}" >/dev/null
fi

docker compose -f "${COMPOSE_FILE}" exec -T "${SERVICE_NAME}" \
  rabbitmqctl set_user_tags "${RABBITMQ_USER}" administrator >/dev/null

docker compose -f "${COMPOSE_FILE}" exec -T "${SERVICE_NAME}" \
  rabbitmqctl set_permissions -p "${VHOST}" "${RABBITMQ_USER}" ".*" ".*" ".*" >/dev/null

echo "RabbitMQ user ${RABBITMQ_USER} is configured for vhost ${VHOST}."
