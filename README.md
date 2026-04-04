# 🚨 MCP Disaster Alert Center

An intelligent AI-powered Model Context Protocol (MCP) server and client demonstrating real-time disaster alert management with LLM-based tool orchestration.

## 🎯 Overview

This project showcases a **disaster alert command center** built with:

- **MCP Server** (.NET 10) - Provides disaster alert tools via JSON-RPC 2.0 over WebSocket
- **AI Agent Client** (.NET 10) - Natural language interface with streaming responses  
- **Google Gemma 4 LLM** - Intelligent tool selection and reasoning
- **Real-time Alerts** - Simulated disaster events streaming to subscribed clients

## ✨ Features

### 🤖 AI Agent Architecture
- **Natural Language Understanding** - Accept free-form user input
- **Agent Reasoning** - LLM explains its understanding in human terms
- **Intelligent Routing** - Selects appropriate tool based on intent
- **Fallback Matching** - Keyword matching when LLM unavailable
- **Streaming Output** - ChatGPT/Claude-style character-by-character streaming

### 🛠️ Available Tools

1. **`subscribe_disaster`** - Subscribe to alert types (earthquake, flood, storm)
2. **`get_alerts`** - Retrieve all active disaster incidents
3. **`incident_summary`** - AI-powered analysis of current incidents

## 🚀 Quick Start

### Prerequisites
- .NET 10 SDK
- Google Gemma 4 API key (get at [Google AI Studio](https://aistudio.google.com/))

### Installation

```bash
# Clone the repository
git clone https://github.com/ndoteddy/mcp-disaster-center.git
cd mcp-disaster-center

# Set up API key (PowerShell)
$env:GEMMA_API_KEY = "your-api-key-here"
```

### Running the Demo

**Terminal 1 - Start the MCP Server:**
```bash
cd server
dotnet run -c Debug
```

**Terminal 2 - Start the AI Agent Client:**
```bash
cd client
dotnet run -c Debug
```

### Example Usage

```
🧠 You: show me active incidents
🤖 Agent: Thinking...
💭 Agent Reasoning: The user wants to see a list of current disaster 
    incidents to stay informed about ongoing emergencies.
🔧 Selected tool: get_alerts
✔️ [10/10] FLOOD in Hilltown @ 08:08:41
   [8/10] EARTHQUAKE in Coastville @ 08:08:43
   [6/10] STORM in Hilltown @ 08:07:27
```

### Commands

```
/help              - Show all commands
/tools             - List available tools
/subscribe <type>  - Subscribe to alerts (earthquake, flood, storm)
/get-alerts        - Get current active alerts
/incident-summary  - AI analysis of active incidents
/quit              - Exit
```

## 🏗️ Architecture

### MCP Protocol
- JSON-RPC 2.0 compliant messaging
- WebSocket transport for real-time communication
- Tool discovery via `tools/list` method
- Tool invocation via `tools/call` method
- Real-time notifications for subscribed clients

### AI Agent Flow
1. **User Input** - Natural language command
2. **Agent Reasoning** - LLM explains understanding
3. **Tool Selection** - LLM chooses appropriate tool
4. **Execution** - Invoke tool via MCP protocol
5. **Results** - Stream response character-by-character

### Components

**Server (MCP Protocol Provider)**
- WebSocket listener on `localhost:5000`
- Tool definitions for disaster management
- Real-time alert generation (2-5 second intervals)
- LLM integration for incident analysis
- Session/subscription management

**Client (AI Agent)**
- Tool discovery on startup
- Dual-stage LLM reasoning
- Graceful fallback to keyword matching
- Streaming character-by-character output
- Error resilience and recovery

## 🔌 API Integration

### Google Gemma 4
- **Endpoint:** `https://generativelanguage.googleapis.com/v1beta/models/gemma-4-31b-it:generateContent`
- **Auth:** Query parameter `key={GEMMA_API_KEY}`
- **Two LLM calls per request:**
  1. Reason about user intent
  2. Select and execute appropriate tool

## 🎓 Learning Outcomes

This project demonstrates:
- MCP protocol implementation and best practices
- WebSocket real-time communication
- LLM integration with fallback patterns
- Streaming/chunked responses
- AI agent reasoning patterns
- Async/await C# patterns
- Real-time command center UX

## 📝 License

MIT License - see LICENSE file for details

## 🙏 Acknowledgments

- **Anthropic** - MCP specification
- **Google AI** - Gemma 4 LLM API
- **.NET Foundation** - ASP.NET Core

---

**Classroom demo for MCP protocol and AI agent orchestration** 🎓
