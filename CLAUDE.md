# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SingleStepViewer is a .NET 9.0 console application built with C#. The project uses implicit usings and nullable reference types enabled.

## Build and Run Commands

Build the project:
```bash
dotnet build
```

Run the application:
```bash
dotnet run
```

Build for release:
```bash
dotnet build -c Release
```

Clean build artifacts:
```bash
dotnet clean
```

Restore NuGet packages:
```bash
dotnet restore
```

## Project Structure

- **SingleStepViewer.csproj** - Main project file targeting .NET 9.0
- **Program.cs** - Application entry point
- **SingleStepViewer.sln** - Visual Studio solution file

## Development Environment

This project is configured for Visual Studio 2022 (v17) but can be developed using any .NET 9.0 compatible IDE or the .NET CLI.
