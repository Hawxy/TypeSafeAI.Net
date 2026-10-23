# Changelog

All notable changes to this project are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Box questions (`Question.Box`, `QuestionSet.Box`) that locate a target in an image, answered by `BoxAnswer` with normalized edges and an optional confidence.
- Image assets: `TypeSafeImage` can be used anywhere content goes, inside text through `TypeSafeContent.FromText` with an interpolated string, or by reference with `TypeSafeContent.WithAssets`. Requests with images are uploaded as `multipart/form-data`.
- Requests with images or box questions send `TypeSafe-Danger-Zone-Images: true`.
- `LongContext` on `TypeSafeClientOptions` and `RequestOptions` sends `TypeSafe-Danger-Zone-Long-Context: true`.
- `TypeSafeRequestTooLargeException` for status 413.

### Fixed

- Request URIs keep any path segments in `BaseUrl` (e.g. `https://host/compatible/` resolves to `https://host/compatible/v1/systemone`), with or without a trailing slash.

## [0.2.0] - 2026-09-17

### Added

- `TypeSafeAI` core client for the TypeSafe System One API (`POST /v1/systemone`, `GET /v1/models`).
- `TypeSafeAI.Extensions.AI` with Microsoft.Extensions.AI guardrail and routing chat-client middleware, an `AIFunction` bridge, and an `IEvaluator` adapter.
- Retries and per-attempt timeouts run on a [Polly](https://www.pollydocs.org) resilience pipeline configured by `RetryPolicy`; `TypeSafeClientOptions.TimeProvider` controls its clock.
- The API key, base URL and default model are set through `TypeSafeClientOptions`, configuration binding, or the `AddTypeSafeClient` overloads; the client does not read environment variables.

[Unreleased]: https://github.com/Hawxy/TypeSafeAI.Net/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/Hawxy/TypeSafeAI.Net/releases/tag/v0.2.0
