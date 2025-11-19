# Multi-Server Usenet Support

NzbDav now supports multiple Usenet servers with automatic failover and redundancy. This feature allows you to configure primary and backup servers, automatically switching to alternative servers when articles are not found or connections fail.

## Features

- **Multiple Server Support**: Configure unlimited Usenet servers with priority-based ordering
- **Automatic Failover**: Seamlessly switches to backup servers when articles aren't found
- **Health Tracking**: Circuit breaker pattern prevents repeated attempts to failed servers
- **Priority System**: Define primary and backup servers with configurable priority levels
- **Connection Pooling**: Independent connection pools per server with configurable limits
- **Backward Compatible**: Existing single-server configurations continue to work
- **Real-time Monitoring**: API endpoints to monitor server health and statistics

## Configuration

### Method 1: Multi-Server Configuration (New)

Configure multiple servers using the `usenet.servers` configuration key with a JSON array:

```json
{
  "usenet.servers": [
    {
      "id": "primary-server",
      "name": "Primary Provider",
      "host": "news.primary.com",
      "port": 563,
      "useSsl": true,
      "username": "your-username",
      "password": "your-password",
      "maxConnections": 50,
      "priority": 0,
      "enabled": true,
      "retentionDays": 3000,
      "groups": "primary,main"
    },
    {
      "id": "backup-server",
      "name": "Backup Provider",
      "host": "news.backup.com",
      "port": 563,
      "useSsl": true,
      "username": "backup-user",
      "password": "backup-pass",
      "maxConnections": 30,
      "priority": 1,
      "enabled": true,
      "retentionDays": 4000,
      "groups": "backup,fill"
    },
    {
      "id": "fill-server",
      "name": "Fill Server",
      "host": "news.fill.com",
      "port": 563,
      "useSsl": true,
      "username": "fill-user",
      "password": "fill-pass",
      "maxConnections": 20,
      "priority": 2,
      "enabled": true,
      "retentionDays": 5000,
      "groups": "fill"
    }
  ]
}
```

### Method 2: Legacy Single-Server Configuration

The existing configuration continues to work for backward compatibility:

```
usenet.host = news.provider.com
usenet.port = 563
usenet.use-ssl = true
usenet.user = username
usenet.pass = password
usenet.connections = 50
```

## Configuration Fields

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `id` | string | No | Auto-generated | Unique identifier for the server |
| `name` | string | Yes | - | Display name for the server |
| `host` | string | Yes | - | Hostname or IP address |
| `port` | number | Yes | 119 | Port number (119 unencrypted, 563 SSL) |
| `useSsl` | boolean | No | false | Enable SSL/TLS encryption |
| `username` | string | Yes | - | Authentication username |
| `password` | string | Yes | - | Authentication password |
| `maxConnections` | number | No | 50 | Maximum concurrent connections |
| `priority` | number | No | 0 | Priority level (0 = highest priority) |
| `enabled` | boolean | No | true | Enable/disable this server |
| `retentionDays` | number | No | 0 | Server retention in days (0 = unknown) |
| `groups` | string | No | "" | Comma-separated tags/groups |

## How Failover Works

1. **Priority Ordering**: Servers are tried in priority order (0 = highest)
2. **Article Not Found**: When an article isn't found on the primary server, the system automatically tries the next server
3. **Connection Failures**: Network errors or authentication failures trigger failover
4. **Circuit Breaker**: After 5 consecutive failures, a server is temporarily disabled for 2 minutes
5. **Automatic Recovery**: Failed servers are automatically retried after the timeout period

## Health Monitoring

### API Endpoint

Monitor server health via the API:

```bash
GET /api/get-server-health
```

**Response:**
```json
{
  "status": true,
  "servers": [
    {
      "id": "primary-server",
      "name": "Primary Provider",
      "host": "news.primary.com",
      "port": 563,
      "priority": 0,
      "maxConnections": 50,
      "isAvailable": true,
      "consecutiveFailures": 0,
      "totalSuccesses": 1523,
      "totalFailures": 12,
      "lastSuccessTime": "2025-11-19T10:30:00Z",
      "lastFailureTime": "2025-11-19T09:15:00Z",
      "lastException": null
    }
  ]
}
```

### Health Statistics

- **isAvailable**: Whether the server is currently available (circuit breaker status)
- **consecutiveFailures**: Number of failures in a row (circuit opens at 5)
- **totalSuccesses**: Total successful operations
- **totalFailures**: Total failed operations
- **lastSuccessTime**: Timestamp of last successful operation
- **lastFailureTime**: Timestamp of last failed operation
- **lastException**: Last error message (if any)

## Use Cases

### Basic Redundancy

Configure two servers with the same priority for load balancing and redundancy:

```json
[
  {
    "name": "Server 1",
    "host": "news1.provider.com",
    "priority": 0,
    ...
  },
  {
    "name": "Server 2",
    "host": "news2.provider.com",
    "priority": 0,
    ...
  }
]
```

### Primary + Fill Server

Use a primary server with a high-retention fill server:

```json
[
  {
    "name": "Primary (3000 days)",
    "priority": 0,
    "retentionDays": 3000,
    ...
  },
  {
    "name": "Fill Server (5000 days)",
    "priority": 1,
    "retentionDays": 5000,
    ...
  }
]
```

### Multiple Backup Layers

Configure multiple fallback levels:

```json
[
  {
    "name": "Primary",
    "priority": 0,
    ...
  },
  {
    "name": "Backup 1",
    "priority": 1,
    ...
  },
  {
    "name": "Backup 2",
    "priority": 2,
    ...
  }
]
```

## Logging

The multi-server system provides detailed logging:

- **INFO**: Server initialization, successful failovers
- **DEBUG**: Operation attempts on each server
- **WARNING**: Connection failures, authentication errors
- **ERROR**: All servers failed for a request

Example log output:

```
[INFO] Initialized server: Primary Provider (news.primary.com:563) with 50 connections at priority 0
[DEBUG] Attempting operation on server: Primary Provider
[WARNING] Article <abc123@news> not found on server Primary Provider
[DEBUG] Attempting operation on server: Backup Provider
[INFO] Successfully retrieved <abc123@news> from fallback server Backup Provider after 1 failures
```

## Migration Guide

### From Single Server to Multi-Server

1. **Export current configuration** via the web UI or API
2. **Create new multi-server config** using your current server as the primary:

```json
{
  "usenet.servers": [
    {
      "name": "Primary Server",
      "host": "YOUR_CURRENT_HOST",
      "port": YOUR_CURRENT_PORT,
      "useSsl": YOUR_CURRENT_SSL_SETTING,
      "username": "YOUR_CURRENT_USER",
      "password": "YOUR_CURRENT_PASS",
      "maxConnections": YOUR_CURRENT_CONNECTIONS,
      "priority": 0,
      "enabled": true
    }
  ]
}
```

3. **Add additional servers** with higher priority values
4. **Update configuration** via the web UI or API
5. **Monitor health** using the `/api/get-server-health` endpoint

### Rollback

To rollback to single-server mode:

1. Remove the `usenet.servers` configuration key
2. Ensure legacy keys exist: `usenet.host`, `usenet.port`, etc.
3. Restart the application

## Performance Considerations

- **Connection Limits**: Total connections = sum of all server `maxConnections`
- **Memory Usage**: Each server maintains its own connection pool and cache
- **Failover Latency**: Failover adds minimal latency (typically <100ms per retry)
- **Circuit Breaker**: Prevents wasted attempts to failed servers

## Best Practices

1. **Set Appropriate Priorities**: Use priority 0 for primary, 1+ for backups
2. **Configure Connection Limits**: Balance between servers based on their capabilities
3. **Monitor Health**: Regularly check server health statistics
4. **Use SSL**: Enable `useSsl: true` for security
5. **Test Failover**: Temporarily disable primary to verify backup servers work
6. **Document Servers**: Use descriptive names and groups for easier management

## Troubleshooting

### All Servers Failing

1. Check network connectivity to all servers
2. Verify credentials are correct
3. Check server health via `/api/get-server-health`
4. Review logs for specific error messages
5. Try resetting health tracking (circuit breakers)

### Slow Performance

1. Increase `maxConnections` on servers
2. Check if circuit breakers are open (servers disabled)
3. Verify network latency to servers
4. Consider reducing number of servers if not needed

### Articles Not Found

1. Verify servers have sufficient retention
2. Check if lower-priority fill servers are configured
3. Review health stats to ensure all servers are being tried
4. Check logs for "Article not found" messages on all servers

## Advanced Configuration

### Environment Variables

For Docker deployments, you can still use environment variables for backward compatibility. The application will use these if no database configuration exists:

```bash
USENET_HOST=news.provider.com
USENET_PORT=563
USENET_SSL=true
USENET_USER=username
USENET_PASS=password
```

### Dynamic Updates

Server configurations can be updated at runtime without restart:

1. Update configuration via API or web UI
2. New connections will use updated settings
3. Existing connections are gracefully drained
4. New connection pools are created

## Technical Architecture

### Components

- **UsenetServerConfig**: Configuration model for individual servers
- **MultiServerNntpClient**: Manages multiple server connections with failover
- **ServerHealthTracker**: Implements circuit breaker pattern
- **ConfigManager**: Handles server configuration with backward compatibility
- **UsenetStreamingClient**: High-level interface for streaming operations

### Failover Flow

```
Request → MultiServerNntpClient
  ↓
Try Server 1 (Priority 0)
  ↓ (if fails)
Try Server 2 (Priority 1)
  ↓ (if fails)
Try Server 3 (Priority 2)
  ↓ (if all fail)
Return error
```

### Health Tracking

```
Operation Success → Reset failure counter
Operation Failure → Increment failure counter
  ↓ (if counter >= 5)
Open Circuit (disable server for 2 minutes)
  ↓ (after 2 minutes)
Close Circuit (re-enable server)
```

## Support

For issues or questions:
- GitHub Issues: https://github.com/odz123/nzbdav/issues
- Documentation: README.md

## License

This feature is part of NzbDav and follows the same license.
