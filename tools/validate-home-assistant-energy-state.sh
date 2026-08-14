#!/usr/bin/env bash
set -euo pipefail

if ! jq -e '
  . as $states |
  def entity($id):
    [$states[] | select(.entity_id == $id)] | if length == 1 then .[0] else null end;
  def number($id):
    try (entity($id).state | tonumber) catch null;
  def metadata($id; $unit; $device_class; $state_class):
    entity($id) as $entity |
    $entity != null
      and $entity.attributes.unit_of_measurement == $unit
      and $entity.attributes.device_class == $device_class
      and $entity.attributes.state_class == $state_class;
  def close($actual; $expected; $tolerance):
    (($actual - $expected) | if . < 0 then -. else . end) <= $tolerance;

  "sensor.hvo_all_pv_power" as $power_helper |
  "sensor.hvo_total_pv_energy_daily" as $daily_helper |
  [
    "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_24xpv_x5fmppt_x5f1_x5fpower",
    "sensor.hvo_3xhvo_3xeg4_13xinverter_x2da_24xpv_x5fmppt_x5f2_x5fpower",
    "sensor.hvo_3xhvo_3xeg4_15xcontroller_x2da_24xpv_x5fmppt_x5f1_x5fpower"
  ] as $power_sources |
  ["sensor.hvo_6500ex_pv_energy_daily", "sensor.hvo_external_mppt_pv_energy_daily"] as $daily_sources |

  metadata($power_helper; "W"; "power"; "measurement")
    and metadata($daily_helper; "kWh"; "energy"; "total_increasing")
    and (if all($power_sources[]; number(.) != null) then
      number($power_helper) != null
        and close(number($power_helper); ($power_sources | map(number(.)) | add); 1.0)
    else entity($power_helper).state == "unavailable" end)
    and (if all($daily_sources[]; number(.) != null) then
      number($daily_helper) != null
        and close(number($daily_helper); ($daily_sources | map(number(.)) | add); 0.002)
    else entity($daily_helper).state == "unavailable" end)
' >/dev/null; then
    printf 'Home Assistant aggregate energy helpers are missing, have invalid metadata, or disagree with numeric sources.\n' >&2
    exit 1
fi

printf 'Home Assistant aggregate energy helpers passed post-restart validation.\n'
