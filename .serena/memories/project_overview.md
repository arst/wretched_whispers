# Wretched Whispers - Project Overview

## Purpose
A dark fantasy TTRPG game engine (Mork Borg-inspired) with AI-powered game mastering via Semantic Kernel.

## Tech Stack
- .NET 8 (Core), .NET 9 (Tests, Semantic, Infrastructure)
- xUnit + Moq for testing
- SQLite + EF Core for persistence
- Microsoft Semantic Kernel for AI plugins
- System.Text.Json for serialization

## Project Structure (under `WrtechedWhispers/` - note the typo)
- `WretchedWhispers.Core` - Domain entities, services, value objects
- `WretchedWhispers.Infrastructure` - SQLite persistence, DI registration
- `WretchedWhispers.Semantic` - SK plugins (Dice, Character, Encounter, Campaign)
- `WretchedWhispers.Tests` - xUnit tests with TestBase providing mocked Dice
- `WretchedWhispers.Orchestration.Console` / `WretchedWhispers.SingleAgent.Console` - Console apps

## Key Commands
- Build: `dotnet build WrtechedWhispers/WrtechedWhispers.sln`
- Test: `dotnet test WrtechedWhispers/WrtechedWhispers.sln`

## Conventions
- Domain entities are sealed classes with JsonConstructor
- Static factory methods (e.g., `Character.Create(...)`)
- Primary constructors for services
- No nullability warnings, nullable reference types enabled
