#!/bin/sh

wait_either() {
    local pid1=$1
    local pid2=$2

    while true; do
        if ! kill -0 "$pid1" 2>/dev/null; then
            wait "$pid1"
            EXITED_PID=$pid1
            REMAINING_PID=$pid2
            return $?
        fi

        if ! kill -0 "$pid2" 2>/dev/null; then
            wait "$pid2"
            EXITED_PID=$pid2
            REMAINING_PID=$pid1
            return $?
        fi

        sleep 0.5
    done
}

# Signal handling for graceful shutdown
terminate() {
    echo "Caught termination signal. Shutting down..."
    if [ -n "$BACKEND_PID" ] && kill -0 "$BACKEND_PID" 2>/dev/null; then
        kill "$BACKEND_PID"
    fi
    if [ -n "$FRONTEND_PID" ] && kill -0 "$FRONTEND_PID" 2>/dev/null; then
        kill "$FRONTEND_PID"
    fi
    # Wait for children to exit
    wait
    exit 0
}
trap terminate TERM INT

# Use env vars or default to 1000
PUID=${PUID:-1000}
PGID=${PGID:-1000}

# Create or reuse group based on PGID
if getent group "$PGID" >/dev/null; then
    EXISTING_GROUP=$(getent group "$PGID" | cut -d: -f1)
    echo "GID $PGID already exists, using group $EXISTING_GROUP"
    GROUP_NAME="$EXISTING_GROUP"
else
    addgroup -g "$PGID" appgroup
    GROUP_NAME=appgroup
fi

# Create or reuse user based on PUID
if getent passwd "$PUID" >/dev/null; then
    EXISTING_USER=$(getent passwd "$PUID" | cut -d: -f1)
    echo "UID $PUID already exists, using user $EXISTING_USER"
    USER_NAME="$EXISTING_USER"
else
    if ! id appuser >/dev/null 2>&1; then
        adduser -D -H -u "$PUID" -G "$GROUP_NAME" appuser
    fi
    USER_NAME=appuser
fi

# Set environment variables
if [ -z "${BACKEND_URL}" ]; then
    export BACKEND_URL="http://localhost:8080"
fi

if [ -z "${FRONTEND_BACKEND_API_KEY}" ]; then
    export FRONTEND_BACKEND_API_KEY=$(head -c 32 /dev/urandom | hexdump -ve '1/1 "%.2x"')
fi

if [ -z "${CONFIG_PATH}" ]; then
    export CONFIG_PATH="/config"
fi

# Recursively update permissions to all $CONFIG_PATH files if needed
chown "$PUID:$PGID" "$CONFIG_PATH"
if [ -f "$CONFIG_PATH/db.sqlite" ]; then
    DB_UID=$(stat -c '%u' "$CONFIG_PATH/db.sqlite")
    DB_GID=$(stat -c '%g' "$CONFIG_PATH/db.sqlite")

    if [ "$DB_UID" -ne "$PUID" ] || [ "$DB_GID" -ne "$PGID" ]; then
        echo "$CONFIG_PATH/db.sqlite ownership mismatch: (uid:$DB_UID gid:$DB_GID) vs expected (uid:$PUID gid:$PGID)"
        echo "Updating ownership of $CONFIG_PATH/* to (uid:$PUID gid:$PGID)"
        chown -R "$PUID:$PGID" "$CONFIG_PATH"
    fi
fi

# Run backend database migration
cd /app/backend
echo "Running database maintenance."
su-exec "$USER_NAME" ./NzbWebDAV --db-migration
if [ $? -ne 0 ]; then
    echo "Database migration failed. Exiting with error code $?."
    exit $?
fi
echo "Done with database maintenance."

# Run backend as "$USER_NAME" in background
su-exec "$USER_NAME" ./NzbWebDAV &
BACKEND_PID=$!

# Wait for backend health check
echo "Waiting for backend to start."
MAX_BACKEND_HEALTH_RETRIES=${MAX_BACKEND_HEALTH_RETRIES:-30}
MAX_BACKEND_HEALTH_RETRY_DELAY=${MAX_BACKEND_HEALTH_RETRY_DELAY:-1}
i=0
while true; do
    echo "Checking backend health: $BACKEND_URL/health ..."
    if curl -s -o /dev/null -w "%{http_code}" "$BACKEND_URL/health" | grep -q "^200$"; then
        echo "Backend is healthy."
        break
    fi

    i=$((i+1))
    if [ "$i" -ge "$MAX_BACKEND_HEALTH_RETRIES" ]; then
        echo "Backend failed health check after $MAX_BACKEND_HEALTH_RETRIES retries. Exiting."
        kill $BACKEND_PID
        wait $BACKEND_PID
        exit 1
    fi

    sleep "$MAX_BACKEND_HEALTH_RETRY_DELAY"
done

# Run frontend as "$USER_NAME" in background
cd /app/frontend
su-exec "$USER_NAME" npm run start &
FRONTEND_PID=$!

# Wait for either to exit
wait_either $BACKEND_PID $FRONTEND_PID
EXIT_CODE=$?

# Determine which process exited
if [ "$EXITED_PID" -eq "$FRONTEND_PID" ]; then
    echo "The web-frontend has exited. Shutting down the web-backend..."
else
    echo "The web-backend has exited. Shutting down the web-frontend..."
fi

# Kill the remaining process
kill $REMAINING_PID

# Exit with the code of the process that died first
exit $EXIT_CODE
