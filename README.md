# Copilot Evaluation Tool

A React + .NET 8 Minimal API application for evaluating LLM (Large Language Model) responses against expected outputs with comprehensive CI/CD pipeline.

## Features

- **CSV File Upload**: Upload validation data with prompts and expected outputs
- **Dynamic Table Management**: Add, edit, and remove validation entries
- **Copilot Integration**: Get responses from Copilot Chat API
- **Similarity Scoring**: Compare expected vs actual outputs with scoring
- **Real-time Status Tracking**: Track validation progress and results
- **Enterprise CI/CD Pipeline**: Automated build, test, and deployment pipeline
- **Blue-Green Deployments**: Zero-downtime deployments with automatic rollback
- **Multi-Environment Support**: Development, staging, and production environments

## Project Structure

```
copilotEval/
├── backend/                  # .NET 8 Minimal API
│   ├── Program.cs           # API endpoints and configuration
│   ├── Properties/          # Launch settings
│   └── CopilotEvalApi.csproj
├── frontend/                # React TypeScript application
│   ├── src/
│   │   ├── components/      # React components
│   │   ├── services/        # API service layer
│   │   ├── types/           # TypeScript interfaces
│   │   └── App.tsx          # Main application component
│   ├── package.json
│   └── vite.config.ts
├── infra/                   # Infrastructure as Code
│   ├── main.bicep          # Main Bicep template
│   ├── modules/            # Bicep modules
│   ├── parameters/         # Environment configurations
│   ├── deploy.sh           # Deployment script
│   └── ci-templates/       # CI/CD reusable templates
├── .github/workflows/      # GitHub Actions workflows
│   ├── ci.yml             # Continuous Integration
│   ├── cd.yml             # Continuous Deployment
│   └── contract-tests.yml # API contract tests
└── README.md
```

## CI/CD Pipeline

The project includes a comprehensive CI/CD pipeline with the following features:

### ✅ Continuous Integration (CI)
- **Automated on**: Pull requests and pushes to main/develop
- **Frontend**: TypeScript compilation, linting, and build validation
- **Backend**: .NET build, testing, and code quality checks
- **Security**: Vulnerability scanning and dependency checks
- **Infrastructure**: Bicep template validation
- **Quality Gates**: All checks must pass before merge

### 🚀 Continuous Deployment (CD)
- **Staging**: Automatic deployment on main branch
- **Production**: Manual approval required
- **Strategy**: Blue-green deployments for zero downtime
- **Rollback**: Automatic on failure, manual trigger available
- **Monitoring**: Health checks and performance monitoring

### 📋 Pipeline Status
```yaml
Environments:
  Staging: ✅ Automated deployment
  Production: 🔒 Manual approval required

Deployment Strategy:
  Type: Blue-Green
  Rollback: Available
  Zero-Downtime: ✅

Security:
  Vulnerability Scanning: ✅
  Dependency Checks: ✅
  Secret Management: ✅
```

## Getting Started

### Prerequisites

- .NET 8 SDK
- Node.js 18+ and npm
- VS Code (recommended)
- Azure CLI (for deployment)

### Backend Setup

1. Navigate to the backend directory:
   ```bash
   cd backend
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Run the API:
   ```bash
   dotnet run
   ```

The API will be available at `http://localhost:5000`

### Frontend Setup

1. Navigate to the frontend directory:
   ```bash
   cd frontend
   ```

2. Install dependencies:
   ```bash
   npm install
   ```

3. Start the development server:
   ```bash
   npm run dev
   ```

The React app will be available at `http://localhost:3000`

## Deployment

### Infrastructure Deployment

The project uses Azure Bicep templates for infrastructure as code:

```bash
# Deploy to development environment
cd infra
./deploy.sh -e dev -g rg-copiloteval-dev -s YOUR_SUBSCRIPTION_ID

# Deploy to staging environment  
./deploy.sh -e staging -g rg-copiloteval-staging -s YOUR_SUBSCRIPTION_ID

# Deploy to production environment
./deploy.sh -e prod -g rg-copiloteval-prod -s YOUR_SUBSCRIPTION_ID
```

### Application Deployment

#### Automatic Deployment (Recommended)
- **Staging**: Automatically deploys when changes are pushed to `main` branch
- **Production**: Manual approval required via GitHub Actions

#### Manual Deployment
```bash
# Build and deploy frontend
cd frontend
npm run build

# Build and deploy backend
cd backend
dotnet publish -c Release
```

### Pipeline Management

```bash
# View deployment status
gh workflow list

# Trigger production deployment
gh workflow run cd.yml --field environment=production

# Monitor deployment logs
gh run watch
```

For detailed deployment procedures, see:
- [Infrastructure Deployment Guide](infra/DEPLOYMENT_SUMMARY.md)
- [Rollback Procedures](infra/ROLLBACK_PROCEDURES.md)
- [Environment Configuration](infra/ENVIRONMENT_CONFIG.md)
- [Troubleshooting Guide](infra/TROUBLESHOOTING.md)

## API Endpoints

- `GET /api/health` - Health check endpoint
- `POST /api/copilot/chat` - Get response from Copilot Chat API
- `POST /api/similarity/score` - Calculate similarity score between texts

## Usage

1. **Upload CSV File**: Use the file upload component to load validation data
   - Expected CSV format: columns named "Prompt" and "Expected Output"
   
2. **Add Entries Manually**: Use the "Add Entry" button to create new validation entries

3. **Process Entries**: The system will automatically process entries through the Copilot API and calculate similarity scores

4. **View Results**: Results are displayed in the validation table with scores and status

## Development

### Frontend Development

- Uses Vite for fast development and building
- TypeScript for type safety
- PapaParse for CSV parsing
- Axios for HTTP requests

### Backend Development

- .NET 8 Minimal API pattern
- CORS enabled for React development
- Simple similarity scoring using Levenshtein distance
- Ready for Copilot Chat API integration

## CI/CD Documentation

- **[CI Pipeline](.github/workflows/ci.yml)**: Automated build, test, and quality checks
- **[CD Pipeline](.github/workflows/cd.yml)**: Deployment automation with blue-green strategy
- **[CI Templates](infra/ci-templates/)**: Reusable workflow components
- **[Environment Config](infra/ENVIRONMENT_CONFIG.md)**: Environment-specific settings
- **[Rollback Procedures](infra/ROLLBACK_PROCEDURES.md)**: Rollback strategies and procedures
- **[Troubleshooting](infra/TROUBLESHOOTING.md)**: Common issues and solutions

## TODO

- [ ] Implement actual Copilot Chat API integration
- [ ] Add authentication and authorization
- [ ] Implement data persistence
- [ ] Add batch processing capabilities
- [ ] Add export functionality for results
- [ ] Improve similarity scoring algorithms

## Contributing

This project is set up for easy development in VS Code with proper TypeScript configuration and .NET tooling.
