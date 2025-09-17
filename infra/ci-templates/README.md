# CI/CD Templates

This directory contains reusable workflow templates and configurations for the CopilotEval CI/CD pipeline.

## Structure

```
ci-templates/
├── build/
│   ├── frontend-build.yml      # Reusable frontend build template
│   ├── backend-build.yml       # Reusable backend build template
│   └── infrastructure-build.yml # Infrastructure validation template
├── test/
│   ├── frontend-test.yml       # Frontend testing template
│   ├── backend-test.yml        # Backend testing template
│   └── integration-test.yml    # Integration testing template
├── security/
│   ├── security-scan.yml       # Security scanning template
│   └── dependency-check.yml    # Dependency vulnerability checking
├── deploy/
│   ├── azure-deploy.yml        # Azure deployment template
│   ├── staging-deploy.yml      # Staging environment deployment
│   └── production-deploy.yml   # Production environment deployment
└── shared/
    ├── setup-dotnet.yml        # .NET setup template
    ├── setup-node.yml          # Node.js setup template
    └── azure-login.yml         # Azure authentication template
```

## Usage

These templates are designed to be used as reusable workflows in GitHub Actions. They promote consistency across different workflows and reduce duplication.

### Example Usage

```yaml
jobs:
  build:
    uses: ./.github/workflows/build/backend-build.yml
    with:
      dotnet-version: '8.x'
      build-configuration: 'Release'
```

## Template Guidelines

1. **Parameterization**: All templates should accept relevant parameters through inputs
2. **Error Handling**: Include proper error handling and meaningful error messages
3. **Caching**: Implement appropriate caching strategies for dependencies
4. **Security**: Follow security best practices for secrets and permissions
5. **Documentation**: Each template should have clear documentation of its purpose and usage