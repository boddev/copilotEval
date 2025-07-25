<!-- Use this file to provide workspace-specific custom instructions to Copilot. For more details, visit https://code.visualstudio.com/docs/copilot/copilot-customization#_use-a-githubcopilotinstructionsmd-file -->

# Copilot Evaluation Tool

This is a React + .NET 9 Minimal API project for evaluating LLM responses.

## Project Structure
- **Frontend**: React with TypeScript, Vite build tool
- **Backend**: .NET 9 Minimal API with CORS enabled for React app

## Key Features
- CSV file upload and parsing for validation data
- Dynamic validation table management
- Copilot Chat API integration
- Similarity scoring between expected and actual outputs
- Real-time validation status tracking

## Frontend Technologies
- React 18 with TypeScript
- Vite for development and building
- PapaParse for CSV parsing
- Axios for HTTP requests

## Backend Technologies
- .NET 9 Minimal API
- CORS configured for localhost:3000
- HTTP client for external API calls
- Built-in similarity scoring using Levenshtein distance

## Development Guidelines
- Use TypeScript for all React components
- Follow React functional component patterns with hooks
- Use async/await for API calls
- Implement proper error handling and loading states
- Maintain separation between frontend UI logic and backend API logic
