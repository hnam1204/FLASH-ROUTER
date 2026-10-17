# ADR-0001: Modular monolith with vertical slices

Status: Accepted for Phase 0A

The existing Blazor host remains the composition root. Foundation owns technical boundaries only. Each feature lives under `src/Modules/<Feature>`, exposes a registration entry point, owns its route and isolated stylesheet, and may publish contracts without exposing private persistence details.

The modules reference Foundation only. Cross-feature calls are reserved for public contracts, read models, or versioned internal events.
