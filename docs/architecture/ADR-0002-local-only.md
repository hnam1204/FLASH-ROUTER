# ADR-0002: Local-only hosting

Status: Accepted for Phase 0A

The application binds to localhost profiles only. `LocalBindingGuard` rejects configured wildcard or LAN bindings. No reverse proxy, deployment target, public listener, or remote service is part of the scaffold.
