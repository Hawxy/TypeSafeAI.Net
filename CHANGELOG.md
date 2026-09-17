# Changelog

All notable changes to this project are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- `TypeSafeAI` core client for the TypeSafe System One API (`POST /v1/systemone`, `GET /v1/models`).
- `TypeSafeAI.Extensions.AI` with Microsoft.Extensions.AI guardrail and routing chat-client middleware, an `AIFunction` bridge, and an `IEvaluator` adapter.
- Retries and per-attempt timeouts run on a [Polly](https://www.pollydocs.org) resilience pipeline configured by `RetryPolicy`; `TypeSafeClientOptions.TimeProvider` controls its clock.
