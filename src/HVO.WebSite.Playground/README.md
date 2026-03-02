# HVO.WebSite.Playground

A Blazor Server web application for testing API endpoints with both Blazor components and traditional MVC views, demonstrating different UI approaches with the same API.

## Features

- **Blazor Server Components**: Interactive UI with C# code-behind
- **Traditional MVC Views**: Classic web development with JavaScript
- **API Versioning**: RESTful API with URL segment versioning
- **HttpClient Integration**: Proper HttpClient configuration with IHttpClientFactory
- **Bootstrap 5**: Modern, responsive UI design
- **Real-time Updates**: Server-side Blazor with SignalR
- **Error Handling**: Comprehensive error handling and logging
- **Side-by-Side Comparison**: Both approaches accessing the same API

## Documentation

- [`docs/projects/website-playground/weather-api-guide.md`](../../docs/projects/website-playground/weather-api-guide.md) – walkthrough of the sample weather integration and REST client usage.

## Project Structure

```
HVO.WebSite.Playground/
├── Components/
│   ├── Layout/                 # Application layout components
│   ├── Pages/                  # Blazor pages
│   │   ├── HealthCheckTest.razor # Blazor API testing page
│   │   └── ...                 # Other Blazor pages
│   └── _Imports.razor          # Global imports
├── Controllers/
│   ├── HomeController.cs       # MVC controller for views
│   └── WeatherController.cs    # API controller
├── Views/
│   ├── Home/
│   │   ├── Index.cshtml        # Landing page with comparison
│   │   └── HealthCheckMVC.cshtml # MVC API testing page
│   └── Shared/
│       └── _Layout.cshtml      # MVC layout
├── wwwroot/                    # Static files
│   ├── css/                    # Stylesheets
│   └── js/                     # JavaScript files
└── Program.cs                  # Application startup
```

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Visual Studio Code or Visual Studio

### Running the Application

1. Clone or download the project
2. Open a terminal in the project directory
3. Run the application:
   ```bash
   dotnet run
   ```
4. Open your browser and navigate to the displayed URL (typically `http://localhost:5136`)

### Testing the API

#### Home Page
Navigate to the application root to see a comparison page:
- **URL**: `http://localhost:5136/`
- **Features**: Side-by-side comparison of MVC vs Blazor approaches

#### MVC UI Testing
1. Navigate to `http://localhost:5136/Home/HealthCheckMVC`
2. Click the "Test Health Check API" button
3. View the response with traditional JavaScript/DOM manipulation

#### Blazor UI Testing
1. Navigate to `http://localhost:5136/health-check-blazor`
2. Click the "Test Health Check API" button
3. View the response with real-time C# updates via SignalR

## API Endpoints

### Weather API
- **URL**: `/api/v1.0/weather/latest`
- **Method**: `GET`
- **Response**: JSON object with latest weather data

### Health Check API
- **URL**: `/health`
- **Method**: `GET`
- **Response**: JSON object with database connectivity status

## Technologies Used

- **.NET 9.0**: Latest .NET framework
- **Blazor Server**: Server-side Blazor with SignalR
- **ASP.NET Core MVC**: Traditional MVC framework
- **Asp.Versioning.Mvc**: API versioning support
- **Bootstrap 5**: CSS framework for responsive design
- **HttpClient**: HTTP client with factory pattern
- **Vanilla JavaScript**: Modern JavaScript without jQuery dependencies
- **Fetch API**: Modern HTTP client for browser-based requests

## Architecture

The application demonstrates a comprehensive comparison between two modern web development approaches:

### 1. **Traditional MVC + JavaScript**
- **Controller**: `HomeController.cs` with action methods
- **Views**: Razor views (`.cshtml`) with server-side rendering
- **Client-Side**: Vanilla JavaScript with Fetch API
- **Communication**: Standard HTTP requests/responses
- **State Management**: Client-side DOM manipulation

### 2. **Blazor Server Components**
- **Components**: Razor components (`.razor`) with C# code-behind
- **Server-Side**: C# runs on the server
- **Client-Side**: Minimal JavaScript, SignalR for communication
- **Communication**: Real-time SignalR connection
- **State Management**: Server-side state with automatic UI updates

### Common Elements
- **API Layer**: Shared RESTful API with versioning
- **Dependency Injection**: IHttpClientFactory for HTTP client management
- **Error Handling**: Comprehensive error handling in both approaches
- **Styling**: Bootstrap 5 for consistent UI across both approaches

## Development

### Adding New MVC Views

1. Create a new action method in `Controllers/HomeController.cs`
2. Create a corresponding view in `Views/Home/[ActionName].cshtml`
3. Use the `_Layout.cshtml` for consistent styling
4. Add JavaScript in a `@section Scripts` block if needed

### Adding New Blazor Pages

1. Create a new `.razor` file in `Components/Pages/`
2. Add the `@page` directive with the route
3. Add `@rendermode InteractiveServer` for server-side interactivity
4. Update the navigation menu in `Components/Layout/NavMenu.razor`

## Comparison Guide

| Feature | MVC + JavaScript | Blazor Server |
|---------|------------------|---------------|
| **Language** | C# + JavaScript | C# Only |
| **Execution** | Client + Server | Server |
| **State Management** | Client-side DOM | Server-side |
| **Real-time Updates** | Manual DOM updates | Automatic via SignalR |
| **Network** | HTTP Requests | SignalR Connection |
| **Development** | Traditional Web | Component-based |
| **Performance** | Lower server load | Higher server load |
| **Offline Support** | Possible | Limited |

## License

This project is for educational and testing purposes.
