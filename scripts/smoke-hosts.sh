#!/usr/bin/env bash
# starts each service host on its own, with every other host stopped, and checks that /health answers.
# usage: dotnet build TravelTracker.slnx -c Release && scripts/smoke-hosts.sh
# needs no database, identity provider or other service: the hosts only validate their settings at startup.
set -u
cd "$(dirname "$0")/.."

configuration="${CONFIGURATION:-Release}"
logs="${SMOKE_LOGS:-$(mktemp -d /var/tmp/traveltracker-smoke.XXXXXX)}"
mkdir -p "$logs"

# name:port
hosts=(Attractions:5104 Transport:5105 Accommodations:5106 Settings:5107 Trips:5108 Users:5101)
failed=0

for entry in "${hosts[@]}"; do
  name="${entry%%:*}"
  port="${entry##*:}"
  dll="src/TravelTracker.$name.Api/bin/$configuration/net10.0/TravelTracker.$name.Api.dll"
  log="$logs/$name.log"
  if [ ! -f "$dll" ]; then
    echo "FAIL $name: $dll is missing, build the solution first"
    failed=1
    continue
  fi
  echo "INFO starting $name alone on port $port (log: $log)"
  # placeholder settings only; nothing connects to them while the host starts
  env ASPNETCORE_URLS="http://127.0.0.1:$port" \
    "ConnectionStrings__$name=Host=127.0.0.1;Port=1;Database=unused" \
    Authentication__Bearer__Authority=https://identity.example.invalid \
    Authentication__Bearer__Issuer=https://identity.example.invalid \
    Authentication__Bearer__Audience=traveltracker-api \
    TripReferences__AccommodationsBaseUrl=http://127.0.0.1:1 \
    TripReferences__AttractionsBaseUrl=http://127.0.0.1:1 \
    TripReferences__TransportBaseUrl=http://127.0.0.1:1 \
    TripReferences__UsersBaseUrl=https://127.0.0.1:1 \
    TripReferences__UsersServiceAccessToken=smoke-test-token \
    dotnet "$dll" >"$log" 2>&1 &
  pid=$!

  status=000
  for _ in $(seq 1 40); do
    kill -0 "$pid" 2>/dev/null || break
    status=$(curl -s -o "$logs/$name.health" -w '%{http_code}' "http://127.0.0.1:$port/health" || true)
    [ "$status" = 200 ] && break
    sleep 0.5
  done

  if [ "$status" = 200 ] && grep -q "\"service\":\"[A-Za-z.]*$name\"" "$logs/$name.health"; then
    echo "PASS $name: /health answered 200 with every other host stopped"
  else
    echo "FAIL $name: /health answered $status; last log lines:"
    tail -n 15 "$log"
    failed=1
  fi
  kill "$pid" 2>/dev/null
  wait "$pid" 2>/dev/null
done

exit "$failed"
