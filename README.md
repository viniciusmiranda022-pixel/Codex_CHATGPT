# Directory Analyzer (Read-Only Active Directory Inventory)

Directory Analyzer is a Windows desktop WPF application for read-only assessment and inventory of Active Directory and associated infrastructure (DNS, GPO, SMB, services, IIS, scheduled tasks, local profiles, local security policy, trusts, proxyAddresses, etc.). The project targets **.NET Framework 4.8** and must remain read-only: it only queries data and never changes Active Directory, GPOs, IIS, local policy, or system configuration.

## Prerequisites
- Windows 10/11 or Windows Server (domain-joined recommended)
- Visual Studio 2019/2022 with **.NET desktop development** workload
- .NET Framework 4.8 targeting pack
- Permissions: read-only domain access. Prefer Windows Integrated authentication.

## Build & Run
1. Open `DirectoryAnalyzer.sln` in Visual Studio.
2. Restore NuGet packages.
3. Build the solution (Debug or Release).
4. Run the application from Visual Studio.

## Architecture Overview
The codebase is moving toward a consistent modular pattern:
- **Collector**: read-only data acquisition (PowerShell/LDAP/WMI).
- **ViewModel**: UI state, async execution, cancellation, progress, and command bindings.
- **Exporters**: centralized CSV/XML/HTML/SQL export logic.
- **LogService**: per-module, per-run logging with levels.

The DNS analyzer has been refactored to this pattern to serve as the template for other modules.

### Key Project Areas
- `Modules/` — Collector interfaces and module-specific collectors.
- `Models/` — Result models used by ViewModels and exports.
- `ViewModels/` — MVVM ViewModels and command infrastructure.
- `Services/` — Shared services (PowerShell, logging, exporting).
- `*.xaml` + `*.xaml.cs` — WPF views; code-behind should only wire ViewModels.

## Logging
Logging is centralized in `Services/LogService`.
- **Per-module** and **per-run** log files.
- Stored in: `Logs/<ModuleName>/<ModuleName>_yyyyMMdd_HHmmss.log`
- Levels: Info, Warn, Error.
- Thread-safe writes with an optional in-memory buffer for UI status usage.

## Exporters
Exports are centralized in `Services/ExportService`:
- **CSV**: UTF-8 with proper escaping.
- **XML**: UTF-8 with safe element names.
- **HTML**: UTF-8 with basic table styling.
- **SQL Server**: sanitizes table/column names, uses bulk copy, and avoids injection risks.

## Read-Only Rules (Non-Negotiable)
- Do **not** write/modify AD, GPOs, IIS, local security policy, services, tasks, registry, or file shares.
- Use minimal permissions; prefer LDAP/AD queries and read-only PowerShell.
- No hardcoded credentials. Use Windows Integrated auth or secure prompting.

## How to Add a New Module (Template)
1. **Create Models**
   - Add result classes in `Models/`.
2. **Collector**
   - Implement `ICollector<T>` in `Modules/<ModuleName>/`.
   - Ensure read-only queries and use cancellation/progress where possible.
3. **ViewModel**
   - Create `<ModuleName>ViewModel` in `ViewModels/`.
   - Use `AsyncRelayCommand` for async execution.
   - Expose `IsBusy`, `StatusMessage`, `ProgressMessage`.
4. **View**
   - Update `<ModuleName>View.xaml` to bind to the ViewModel.
   - Code-behind should set `DataContext` only.
5. **Exports**
   - Use `ExportService.ExportToCsv/Xml/Html/Sql`.
6. **Logging**
   - Create a logger via `LogService.CreateLogger("<ModuleName>")`.
7. **Hook Up Navigation**
   - Update `MainWindow.xaml.cs` to load the new view.

## Notes on Compatibility
- Preserve the .NET Framework target.
- Avoid introducing new dependencies unless necessary and compatible.
- Keep changes incremental and safe.
