# OAuth-Protected MCP Server

A .NET 9 application that combines an OAuth 2.0 authorization server with a Model Context Protocol (MCP) server in a single process. This server provides secure access to Enphase solar data analysis tools through authenticated MCP connections.

As part of my New Learning Journey, I needed an MCP server requiring authorization to test against Claude Code and
an Azure AI Foundry Agent.

This was created by combinging these two C# MCP SDK repos as they existed in July/August 2025:

https://github.com/modelcontextprotocol/csharp-sdk/tree/main/samples/ProtectedMcpServer
https://github.com/modelcontextprotocol/csharp-sdk/tree/main/tests/ModelContextProtocol.TestOAuthServer

Note: ignore the SSE part of the names here, this is now an Http Transport MCP Server.

Please see my LinkedIn post for more info:
https://www.linkedin.com/feed/update/urn:li:share:7364031524003815424/

## Features

### Core Functionality
- **OAuth 2.0 authentication** with JWT tokens
- **MCP server** with Enphase solar data tools
- **Single application** - no separate OAuth server needed
- **Azure AI Foundry** agent compatible
- **Claude Code** compatible (manual auth flow)
- **ngrok support** for external access

### OAuth 2.0 Capabilities
- **Full OAuth 2.0 flow support** - Authorization code, client credentials, refresh tokens
- **PKCE support** (S256 code challenge)
- **Dynamic client registration** (basic RFC 7591 support)
- **Token introspection** endpoint
- **Multiple grant types** (authorization_code, client_credentials, refresh_token)
- **8-hour token expiration** (configurable)

### MCP Integration
- **Stateless MCP transport** - Required for Azure AI Foundry compatibility
- **Protected endpoints** - All MCP tools require valid JWT tokens
- **Enphase solar data tools** - Analyze solar panel performance and energy production

## Architecture

### URL Structure
- **OAuth endpoints** at root (`/.well-known/*`, `/token`, `/register`, `/authorize`)
- **MCP endpoints** at `/mcp`
- **Single port (7071)** with ngrok tunneling for external access
- **Clear URL separation** - public ngrok URLs vs local Kestrel binding

### Key Implementation Details
- **Persistent RSA keys** - RSA signing keys saved to file and reloaded on restart
- **Hybrid OAuth storage** - Core OAuth server in-memory with persistent client registration
- **Client persistence** - Dynamically registered clients saved to `oauth-clients.json`
- **Audience/Issuer validation** - proper JWT claim validation
- **File-based persistence** - RSA keys and registered clients survive server restarts

## Usage

### Quick Start
1. **Update ngrok URL** in `Program.cs` (line 39):
   ```csharp
   var publicBaseUrl = "https://your-ngrok-domain.ngrok-free.app/";
   ```

2. **Start ngrok tunnel**:
   ```bash
   ngrok http 7071 --domain=your-static-domain.ngrok-free.app
   ```

3. **Run the application**:
   ```bash
   dotnet run
   ```

4. **Get demo token** (displayed on startup):
   ```bash
   curl -k -d "grant_type=client_credentials&client_id=demo-client&client_secret=demo-secret&resource=https://your-ngrok-domain.ngrok-free.app/mcp/" -X POST https://your-ngrok-domain.ngrok-free.app/token
   ```

### Azure AI Foundry Integration
- **Manual token workflow** - Generate token via curl, copy to agent configuration
- **Stateless transport** - Compatible with Azure AI agent requirements
- **Bearer token authentication** - Add token to agent HTTP headers

### Claude Code Integration
- **Manual auth flow** - Copy/paste authentication URL when browser doesn't open
- **OAuth discovery** - Automatic detection of OAuth endpoints
- **Dynamic client registration** - Supports automatic client setup

## Data Tools

### Enphase Solar Analysis
- **Sample data included** - Contains Enphase solar CSV files for immediate testing
- **Command-line data path** - Optional argument to specify custom data directory
- **Panel performance analysis** - Individual panel efficiency and comparison
- **Energy production tracking** - System-level solar generation data

### Data Directory
```bash
# Use included sample data
dotnet run

# Use custom data directory
dotnet run /path/to/your/enphase/data
```

## Configuration

### Pre-configured Clients
- **Demo client**: `demo-client` / `demo-secret`
- **Test refresh client**: `test-refresh-client` / `test-refresh-secret` (for testing token refresh)

### Endpoints
- **OAuth Server**: `https://your-domain/` (root)
- **MCP Server**: `https://your-domain/mcp/`
- **Token endpoint**: `https://your-domain/token`
- **Authorization**: `https://your-domain/authorize`
- **Registration**: `https://your-domain/register`
- **JWKS**: `https://your-domain/.well-known/jwks.json`

## Development Notes

### OAuth Implementation
- **Basic RFC 7591 support** - Dynamic client registration
- **PKCE required** - S256 code challenge method only
- **Resource validation** - Validates audience claims in JWT tokens
- **Refresh token support** - Long-lived access with token rotation

### MCP Server Features
- **Protected resources** - All endpoints require valid JWT authentication
- **Tool registration** - Automatic discovery of available data analysis tools
- **HTTP transport** - Stateless operation for compatibility with AI agents

### Security Considerations
- **Development use only** - File-based storage not suitable for production
- **Persistent RSA keys** - Keys saved to local files for development convenience
- **Local binding** - Server binds to localhost, external access via ngrok only
- **Client persistence** - OAuth client registrations persist across restarts

## Troubleshooting

### Common Issues
- **Key mismatch errors** - Ensure using tokens generated after server restart
- **Audience validation failures** - Verify ngrok URL matches resource configuration
- **Browser not opening** - Copy/paste authentication URL manually for Claude Code
- **404 errors** - Check that OAuth endpoints are mapped at root level

### Token Testing
```bash
# Validate token manually
curl -H "Authorization: Bearer YOUR_TOKEN" https://your-domain.ngrok-free.app/mcp/
```

## Based On

This implementation combines and extends code from the [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk):
- **ProtectedMCPServer sample** - Base MCP server with OAuth integration
- **TestOAuthServer** - In-memory OAuth 2.0 authorization server

### Key Modifications
- **Merged architecture** - Single process for both OAuth and MCP servers
- **ngrok compatibility** - External access through tunneling
- **Azure AI Foundry support** - Stateless transport configuration
- **Enhanced OAuth flow** - Core OAuth 2.0 flows with additional endpoints
- **Persistent state** - RSA keys and client registrations survive server restarts
- **GitHub Copilot compatibility** - Handles clients that expect OAuth state persistence


## Copyright and License

### Code

Copyright (�) 2025  Jzuras

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.


## Trademarks

Enphase(R) and Envoy(R) are trademarks of Enphase Energy(R).

All trademarks are the property of their respective owners.

Any trademarks used in this project are used in a purely descriptive manner and to state compatibility.