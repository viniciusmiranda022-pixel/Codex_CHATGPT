Directory Analyzer - Codebase Architecture Overview
I've analyzed the vini0199/Directory-Analyzer repository and here's a comprehensive architectural overview for new engineers:

🎯 Project Purpose
Directory Analyzer is a C# WPF desktop application designed for analyzing Windows Active Directory environments. It provides multiple specialized analyzers to examine different aspects of AD infrastructure including DNS, Group Policy Objects (GPOs), SMB shares, scheduled tasks, user profiles, services, security policies, IIS application pools, domain trusts, and proxy addresses.

🏗️ Technology Stack
Framework: .NET Framework 4.8 with WPF (Windows Presentation Foundation)

Language: C# (latest version enabled)

UI Framework: WPF with MahApps.Metro for modern styling

PowerShell Integration: Microsoft.PowerShell.5.ReferenceAssemblies for executing AD queries

Database: SQL Server integration via System.Data.SqlClient

Directory Services: System.DirectoryServices and System.DirectoryServices.AccountManagement

System Management: System.Management and System.ServiceProcess.ServiceController

📁 Project Structure
Main Application Files
App.xaml - Application entry point and global resources

MainWindow.xaml - Main UI layout with navigation menu

MainWindow.xaml.cs - Main window logic and view navigation

Analyzer Views (10 specialized modules)
Each analyzer follows the same pattern with .xaml (UI) and .xaml.cs (logic) files:

DnsAnalyzerView - DNS zones, records, and forwarders analysis

GpoAnalyzerView - Group Policy Objects analysis

SmbAnalyzerView - SMB shares analysis

ScheduledTasksAnalyzerView - Windows scheduled tasks

LocalProfilesAnalyzerView - User profiles analysis

InstalledServicesAnalyzerView - Windows services analysis

LocalSecurityPolicyAnalyzerView - Security policies

IisAnalyzerView - IIS application pools

TrustAnalyzerView - Domain trusts

ProxyAddressAnalyzerView - Email proxy addresses

Core Services
PowerShellService.cs - PowerShell script execution engine

ExportService.cs - Data export to SQL Server

LogService.cs - Application logging system

SqlManagerService.cs - SQL database management

Dialogs
SqlConnectionDialog.xaml - SQL Server connection configuration
🔧 Architecture Patterns
1. Modular Analyzer Pattern
Each analyzer is a self-contained UserControl that follows this consistent structure:

Data Collection: Uses PowerShell scripts to query AD/Windows systems

UI Presentation: Displays results in tabbed DataGrids

Export Capabilities: Supports CSV, XML, HTML, and SQL export formats

Logging: Comprehensive logging for debugging and audit trails

2. Service-Oriented Architecture
The application uses dedicated service classes for cross-cutting concerns:

PowerShellService: Centralized PowerShell execution with async support

ExportService: Unified data export functionality

LogService: Centralized logging to local files

SqlManagerService: Database creation and management

3. Navigation Pattern
Main Window: Acts as a shell with a left navigation menu

Content Area: Dynamically loads analyzer views based on menu selection

Tab-based Results: Each analyzer presents results in multiple tabs

🔄 Key Workflows
1. Application Startup
App.xaml initializes the WPF application

MainWindow loads with navigation menu

First analyzer (DNS) is selected by default

2. Data Collection Workflow
User selects an analyzer from the navigation menu

User clicks "Run Analysis" button

Analyzer executes PowerShell scripts via PowerShellService

Results are parsed and displayed in DataGrids

UI animations provide visual feedback

Status updates and logging occur throughout

3. Export Workflow
User selects data from any analyzer tab

Chooses export format (CSV, XML, HTML, or SQL)

For SQL exports: SqlConnectionDialog captures connection details

ExportService handles the actual data export

Success/error feedback provided to user

🎨 UI Architecture
Layout Structure
Two-column layout: Navigation menu (200px) + Content area (flexible)

Consistent styling: Uses MahApps.Metro theme with custom color palette

Responsive design: Content area adapts to window size

Visual Design
Color scheme: Primary blue (#0078D7), dark menu (#1f1f1f), light content (#f7f7f7)

Typography: Sans-serif fonts with clear hierarchy

Interactive elements: Hover effects and selection states

🔐 Security & Data Handling
Authentication
Relies on Windows integrated authentication for AD access

PowerShell scripts run under current user context

SQL connections support both Windows and SQL authentication

Data Export
Local files: CSV, XML, HTML exports to user-selected locations

SQL Server: Dynamic table creation with timestamp-based naming

Logging: All operations logged to %LocalAppData%\\DirectoryAnalyzer\\

🚀 Entry Points for New Developers
Start Here
MainWindow.xaml.cs - Understand navigation logic

DnsAnalyzerView.xaml.cs - Study the analyzer pattern

PowerShellService.cs - Learn PowerShell integration

Common Development Tasks
Adding new analyzer: Create new *AnalyzerView.xaml/.cs files following existing pattern

Modifying exports: Update ExportService.cs for new formats

UI changes: Modify individual analyzer XAML files

PowerShell scripts: Update embedded scripts in analyzer code-behind files

📋 Dependencies & Requirements
Runtime Requirements
Windows OS with .NET Framework 4.8

PowerShell 5.0+ with AD modules

Access to Active Directory environment

Optional: SQL Server for data export

Development Requirements
Visual Studio with WPF support

Windows SDK

PowerShell development tools

This architecture provides a solid foundation for Windows AD analysis with a clean separation of concerns, consistent patterns, and extensible design for adding new analyzers.