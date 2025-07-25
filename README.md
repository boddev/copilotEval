# Copilot Evaluation Tool

A React + .NET 9 Minimal API application for evaluating LLM (Large Language Model) responses against expected outputs.

## Features

- **CSV File Upload**: Upload validation data with prompts and expected outputs
- **Dynamic Table Management**: Add, edit, and remove validation entries
- **Copilot Integration**: Get responses from Copilot Chat API
- **Similarity Scoring**: Compare expected vs actual outputs with scoring
- **Real-time Status Tracking**: Track validation progress and results

## Project Structure

```
copilotEval/
├── backend/                 # .NET 9 Minimal API
│   ├── Program.cs          # API endpoints and configuration
│   ├── Properties/         # Launch settings
│   └── CopilotEvalApi.csproj
├── frontend/               # React TypeScript application
│   ├── src/
│   │   ├── components/     # React components
│   │   ├── services/       # API service layer
│   │   ├── types/          # TypeScript interfaces
│   │   └── App.tsx         # Main application component
│   ├── package.json
│   └── vite.config.ts
└── README.md
```

## Getting Started

### Prerequisites

- .NET 9 SDK
- Node.js 18+ and npm
- VS Code (recommended)

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

- .NET 9 Minimal API pattern
- CORS enabled for React development
- Simple similarity scoring using Levenshtein distance
- Ready for Copilot Chat API integration

## TODO

- [ ] Implement actual Copilot Chat API integration
- [ ] Add authentication and authorization
- [ ] Implement data persistence
- [ ] Add batch processing capabilities
- [ ] Add export functionality for results
- [ ] Improve similarity scoring algorithms

## Contributing

This project is set up for easy development in VS Code with proper TypeScript configuration and .NET tooling.
