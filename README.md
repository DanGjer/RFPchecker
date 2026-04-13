# RFPchecker

A Revit extension for electrical outlet compliance verification in building spaces. RFPchecker compares electrical fixture and data device counts against dRofus (building information database) requirements and provides visual feedback.

## Features

- **Multi-mode operation:**
  - **Analyze** — Evaluate all spaces on the active level
  - **Analyze Selected** — Evaluate only user-selected spaces
  - **Reset** — Clear colors and remove analysis notes

- **Outlet categorization:**
  - Normal (standard 230V outlets)
  - Emergency (nødkraft outlets)
  - UPS (uninterruptible power supply outlets)
  - Data (network/communication outlets)
  - Dedicated outlets (marked with `Formaal` parameter)
  - Undefined outlets (missing power type designation)

- **Compliance status indicators:**
  - **OK** (green) — Actual outlets meet RFP requirements
  - **Under RFP** (red) — Deficit of required outlets
  - **Over RFP** (blue) — Excess outlets installed
  - **Udef. uttak** (orange) — Undefined outlets present (compliance cannot be assessed)
  - **Unmatched** (grey) — No matching room in dRofus

- **Space visualization:**
  - Color-filled spaces indicating compliance status
  - Compact text notes showing outlet counts and status
  - Level-aware filtering (processes only spaces on the active view's associated level)
  - Selection-aware processing for targeted analysis

- **Data safety:**
  - Only deletes RFPchecker-specific text notes (other project notes are preserved)
  - View-scoped operations (only affects active view)

## Requirements

- **Revit:** 2024 or later
- **dRofus API:** Client v1.0.11
- **.NET:** 8.0-windows

## Parameters

### Revit Space Parameters
- `BSN_RomNrFunk` (instance) — Room identifier for cross-matching with dRofus

### Revit Electrical Fixture Parameters
- `SUS_Antall Stikkontaktuttak` (type) — Outlet count
- `Krafttype` (instance) — Power type (Normal, Emergency, UPS, Data, or blank for undefined)
- `Formaal` (instance, optional) — Purpose/dedication (presence indicates a dedicated outlet)

### Revit Data Device Parameters
- `SUS_Antall Datauttak` (type) — Data outlet count
- `Formaal` (instance, optional) — Purpose/dedication

### dRofus Fields
- `room_func_no` — Room function number (matched to `BSN_RomNrFunk`)
- `room_data_20101610` — Normal outlets required
- `room_data_20102210` — Emergency outlets required
- `room_data_20102310` — UPS outlets required
- `room_data_21101010` — Data outlets required

## Usage

1. **Prepare view:**
   - Create a plan view on the desired level
   - Set the view's associated level (e.g., "76.03")
   - Optionally hide/isolate categories and elements as needed

2. **Run analysis:**
   - Open the RFPchecker extension dialog
   - Select operation mode:
     - **Analyze** — colors all spaces on the level
     - **Analyze Selected** — select spaces first, then choose this mode
     - **Reset** — removes previous analysis
   - Execute

3. **Interpret results:**
   - Space fill colors indicate status
   - Text notes show outlet breakdown and compliance status
   - Summary message displays count of each status category

## Release Build

- Command: `dotnet publish -c "Release 2025"`
- VS Code task: `Revit 2025 Release`
- Output folder: `bin\Release 2025\net8.0-windows\publish`
- Main assembly: `RFPchecker.2025.0.1.5.dll`

The release publish includes the extension assembly and its required runtime dependencies.

## Text Note Format

```
[Room Name] [Room Number]
RFP Status: [OK|Under RFP|Over RFP|Udef. uttak|Unmatched]
N:x/y Nød:x/y U:x/y D:x/y
DE:x DD:y MKT:z
```

Where:
- `N` — Normal outlets (actual/required)
- `Nød` — Emergency outlets (actual/required)
- `U` — UPS outlets (actual/required)
- `D` — Data outlets (actual/required)
- `DE` — Dedicated electrical outlets
- `DD` — Dedicated data outlets
- `MKT` — Outlets with missing power type

## Architecture

- **RFPchecker.cs** — Main command logic, status determination, coloring, and text notes
- **Revit.cs** — Domain models (RevitSpace, RevitElectricalFixture, RevitDataDevice) and static collectors
- **Drofus.cs** — API response models (DrofusRoom, DrofusRoomResponse)
- **UserArgs.cs** — User-facing arguments and mode enum
- **GlobalUsings.cs** — Global namespace declarations

## dRofus Integration

Connects to dRofus API via `dRofusClient` (v1.0.11) with filter for building "Bygg 76". Room matching uses the `room_func_no` field from API response.

## Notes

- All analysis is non-destructive; colors and notes can be cleared with Reset mode
- Spaces are filtered by the active view's associated level
- Undefined outlets (missing `Krafttype`) trigger orange status regardless of numeric compliance
- Text notes are stored in a dedicated `RFPchecker_Note` type for safe cleanup
