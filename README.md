# FinancialMcp

## Overview

FinancialMcp is a personal financial management platform focused on building a reliable financial history from multiple data sources and enabling future AI-powered financial analysis.

The project started as a reconciliation tool but evolved into a broader financial intelligence platform, the Financial Copilot.

The long-term goal is to provide a financial MCP (Model Context Protocol) capable of understanding personal finances, answering historical questions, generating forecasts, detecting spending patterns, and helping make better financial decisions.

> **`FinancialMcp-Roadmap.md` is the source of truth for this project.** Before implementing new features, read it in full. Any decision about architecture, data model, or workflow must align with the vision defined there. This README gives a high-level orientation only.

---

# Vision

This is NOT a traditional accounting system, and it is NOT a bank-reconciliation tool.

The system is centered on **reviewing and classifying financial movements**, not on matching them against an external record. Any movement that enters the system — regardless of its source — goes through a review process where the user classifies it along its dimensions (see "Movement Classification Model" below). Matching against an external record (a personal Excel, for example) is only an optional aid to speed up that classification when a coincidence exists — it is never the goal of the process. A reviewed movement without any external counterpart is just as valid and complete a result as one that was matched.

The objective is to create a personal financial knowledge base that combines:

* Bank account movements
* Credit card transactions
* Legacy/manual expense records (migration aid only, see below)
* Fixed expenses (planned, see Roadmap)
* Historical spending behavior

into a single source of truth.

Eventually the platform will power an AI assistant capable of answering questions such as:

* How much did I spend on pharmacy expenses this year?
* How much do I spend on cigarettes per month?
* Which categories increased the most?
* What is my expected spending next month?
* Which fixed expenses are increasing over time?
* How much money do I need to maintain my current lifestyle?

---

# Core Philosophy

Bank and credit card movements are considered the financial source of truth.

The personal Excel spreadsheet is **not** part of the long-term vision of the system. Its role is exclusively a **migration/compatibility mechanism**, useful only while historical data is transitioned into FinancialMcp. New flows (fixed expenses, counterparties, categories) are designed without depending on Excel data.

The system prioritizes what actually happened in bank accounts and credit cards over manual spreadsheets.

---

# Data Sources

## Bank Statements

Examples:

* FARMACIA AMANCAY
* CAMPO VERDE
* MERCADOPAGO
* TRANSFERENCIA

These represent real financial events.

---

## Credit Card Transactions

Examples:

* Supermarket purchases
* Pharmacy purchases
* Online shopping
* Fuel expenses

These are also considered real financial events.

---

## Legacy Imported Expenses

Imported from personal Excel spreadsheets. Used only as a migration/compatibility aid for historical data — not part of the system's long-term data model.

Examples:

* Pharmacy
* Grocery Store
* Cigarettes
* Entertainment

Characteristics:

* Usually recorded manually the same day.
* Dates may differ from bank dates.
* Amounts may differ slightly.
* Descriptions may differ significantly.

Example:

Excel:

Farmacia
$50.000

Bank:

FARMACIA AMANCAY
$49.974

Both represent the same expense — the bank movement is the one that gets classified; the Excel record only helps suggest how.

---

## Fixed Expenses (planned)

Fixed expenses represent recurring financial commitments (rent, internet, insurance, electricity, gas, mobile phone) managed natively in the system, independent of the Excel. This module is planned — see the Roadmap's "Gastos Fijos" section for its current status.

---

# Movement Classification Model

Every movement that gets classified is described through **four independent dimensions**:

* **Movement Type** — what happened (Purchase, Transfer, Payment, Receipt, Fee, Interest, Refund, Adjustment, Other).
* **Financial Impact** — how it affects net worth (Expense, Income, Internal Movement, Debt Payment/Financing). Only Expense counts toward net spending metrics.
* **Category** — what the money was used for. An administrable entity (CRUD), not a closed enum.
* **Counterparty** — who or what the movement relates to. An administrable entity (CRUD) with suggested default values (category, movement type, financial impact) to speed up classification of recurring movements.

Matching against a legacy/manual record can group movements 1↔1, N↔1, 1↔N, or N↔M as a suggestion aid, but the classification itself is always expressed through the four dimensions above.

---

# Movement Classification States

A classified movement (`ClassifiedMovement`) only has two possible states — there is no "Pending" state, because every row in that table represents financial truth already verified by the user:

## Confirmed

The user accepted a suggested match against a legacy/manual record.

## Reviewed

The user classified the movement manually, with no external counterpart. Examples: personal transfers, gifts, internal transfers, bank fees, interests.

There is no hierarchy between `Confirmed` and `Reviewed` — both are, in essence, classified movements. The distinction is kept only for traceability (`ProcessingSource`), not as a functional difference.

---

# Current Architecture

Main layers:

* Domain
* Application
* Infrastructure
* API

The project follows Clean Architecture principles.

Business logic should remain inside Application and Domain layers.

Endpoints should remain thin and delegate behavior to commands, queries, handlers, or services depending on the existing module architecture.

---

# Current Priorities

See `FinancialMcp-Roadmap.md` for the full, up-to-date phase breakdown. Summary:

## Phase 1 - Data Foundation (in progress)

Goals: clean, classified, correctly persisted data — bank/card import, classification model (Movement Type, Financial Impact, Category, Counterparty), N↔M review support.

## Phase 2 - Financial Visibility

Goals: metrics dashboard, fixed expenses module, budgets, deviation alerts.

## Phase 3 - Planning

Goals: cash flow projection, upcoming due dates, financial goals, net worth.

## Phase 4 - Investments and Net Worth

Goals: investment tracking, returns, asset distribution.

## Phase 5 - AI and MCP

Goals: MCP tools for planning and net worth, local model integration, natural language answers, proactive recommendations.

---

# Long-Term MCP Vision

The MCP should eventually answer questions such as:

* How much did I spend on pharmacy expenses this year?
* How much did I spend on cigarettes?
* Which category increased the most?
* Which fixed expenses are growing?
* Which expenses were never categorized?
* What is my expected spending next month?
* What budget should I plan for next month?
* What are the biggest opportunities to save money?

---

# Repository Guidelines

Before implementing new features:

1. Read `FinancialMcp-Roadmap.md` in full — it is the source of truth.
2. Verify the proposal answers at least one question from the Roadmap's philosophy.
3. Verify it does not contradict a frozen data-model decision.
4. Preserve the existing architecture.
5. Avoid bypassing application services.
6. Keep business rules inside Domain/Application layers.
7. If something in the code contradicts the Roadmap, flag it explicitly before continuing.

This document should be considered the starting context for both developers and AI assistants working on the project.
