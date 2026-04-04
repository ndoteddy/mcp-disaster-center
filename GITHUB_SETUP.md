## 📋 Steps to Commit to GitHub

Git is not currently available in your PATH. Here are the steps to push this project to GitHub:

### Option 1: Using GitHub Desktop (Recommended for Windows)
1. Download [GitHub Desktop](https://desktop.github.com/)
2. Install and log in with your GitHub account
3. Click "File → Add Local Repository"
4. Select this folder: `c:\Users\nandos\Desktop\Github\ndo-mcp`
5. GitHub Desktop will auto-detect files to commit
6. Enter commit message: "Initial commit: MCP Disaster Alert Center with AI agent"
7. Click "Publish repository"
8. Use the web URL: `https://github.com/ndoteddy/mcp-disaster-center.git`

### Option 2: Using Git Command Line (After Installing Git)

1. **Install Git for Windows:**
   - Download from [git-scm.com](https://git-scm.com/)
   - Run the installer with default settings

2. **Open PowerShell and run:**

```powershell
cd "c:\Users\nandos\Desktop\Github\ndo-mcp"

# Initialize git repository
git init

# Add all files
git add .

# Create initial commit
git commit -m "Initial commit: MCP Disaster Alert Center with AI agent"

# Set remote (use your GitHub URL)
git remote add origin https://github.com/ndoteddy/mcp-disaster-center.git

# Create main branch if needed
git branch -M main

# Push to GitHub
git push -u origin main
```

### What Will Be Committed

✅ **Files included:**
- `Server.cs` - MCP server implementation
- `Client.cs` - AI agent client  
- `Server.csproj` - .NET 10 project file
- `Client.csproj` - .NET 10 project file
- `README.md` - Full documentation
- `.gitignore` - Git ignore rules

❌ **Files excluded (via .gitignore):**
- `bin/` - Build outputs
- `obj/` - Object files
- `.vs/` - Visual Studio settings
- `.vscode/` - VS Code settings

### Repository URL

```
https://github.com/ndoteddy/mcp-disaster-center.git
```

After pushing, your project will be live on GitHub with full documentation! 🚀
