# Contributing

## Set up your system

The project consists of two sub projects: frontend and backend
Both share some necessary environment variables.

Environment variables:

```bash
export CONFIG_PATH=~/workspace/nzbdav/backend/publish/config
export FRONTEND_BACKEND_API_KEY=$(head -c 32 /dev/urandom | hexdump -ve '1/1 "%.2x"')
export BACKEND_URL=http://localhost:5000
```

You need some packages in order to run the project:

- dotnet-sdk
- aspnet-runtime
- nodejs
- npm

Example installation for Arch based systems:

```bash
sudo pacman -S dotnet-sdk aspnet-runtime nodejs npm
```

## Build / run backend

```bash
cd backend

# Build
dotnet publish -c Release -o ./publish

# Create database
mkdir publish/config
./publish/NzbWebDAV --db-migration

# Run backend
./publish/NzbWebDAV
```

## Build / serve frontend

```bash
cd frontend

# Install dependencies
npm install

# Run / serve frontend with hot module replacement
npm run dev
```

## Contributing

You might check types before creating a PR:

```bash
cd frontend
npm run typecheck
```
