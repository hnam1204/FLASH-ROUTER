# ADR-0006: Single-Page Account-First Interface Architecture

Status: Accepted for UI Phase A
Date: 2026-10-15

## Context
Phase 0A scaffolded 11 independent Razor modules, each mapped to a distinct route (`/accounts`, `/dashboard`, etc.) in an admin-sidebar layout.
The approved product vision and visual design require FLASH ROUTER to be a modern, minimal, One-Page Account-First application. The interface centers entirely around the account workspace with a header, filter bar, split two-column account list/detail view, and modal/drawer workflows, eliminating sidebars, KPI dashboard widgets, and multi-page routing.

## Decision
1. **Host Composition Root**: The host application at `/` (`Home.razor` & `MainWorkspace.razor`) serves the unified Single-Page workspace.
2. **Feature Ownership**: Each of the 11 modules retains strict ownership over its business domain, typed contracts, feature UI components, and isolated CSS (`.razor.css`).
3. **Component Slices**:
   - `FlashAI.Modules.Accounts`: Owns `AccountList`, `AccountListItem`, `AccountDetailPanel`, `AccountInformationTab`, `CreateAccountDialog`, `EditAccountForm`, and account-related modal guards.
   - `FlashAI.Modules.Connections`: Owns `ConnectionTab`, `ReconnectDialog`, `DisconnectDialog`.
   - `FlashAI.Modules.Subscriptions`: Owns `SubscriptionTab`, `SubscriptionSummaryCard`, `SubscriptionEditDialog`.
   - `FlashAI.Modules.Authenticator`: Owns `AuthenticatorTab`, `VaultUnlockDialog`, `AddTotpDialog`.
   - `FlashAI.Modules.Reminders`: Owns `ReminderTab`, `ReminderEditorDialog`.
   - `FlashAI.Modules.Activity`: Owns `ActivityTab`, `ActivityDetailsDrawer`.
   - `FlashAI.Modules.ImportExport`: Owns `ImportExportDialog`, `ExportOptionsDialog`.
   - `FlashAI.Modules.BackupRestore`: Owns `BackupRestoreDialog`.
   - `FlashAI.Modules.Settings`: Owns `SettingsDialog` (Appearance, Privacy, General).
   - `FlashAI.Modules.Diagnostics`: Owns `DiagnosticsPanel`.
   - `FlashAI.Modules.Dashboard`: Retained as compatibility module.
4. **Route Compatibility**: Existing module routes (`/accounts`, `/connections`, etc.) remain in place to preserve Phase 0A architecture tests and redirect or link to the central workspace.
5. **Circuit-Scoped UI State & Development Preview**: During development preview, an in-memory circuit-scoped adapter provides realistic, safe demo accounts (e.g., `account01@example.com` to `account12@example.com`) without passwords, live tokens, or SQLite mutations.
6. **Codex Backend Contracts**: Strong C# typed contracts (`IAccountWorkspaceReader`, `IAccountCommandPort`, `IConnectionUiPort`, etc.) define the boundary for future backend implementation.
