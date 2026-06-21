# FinancialMcp

## Overview

FinancialMcp is a personal financial management platform focused on building a reliable financial history from multiple data sources and enabling future AI-powered financial analysis.

The project started as a reconciliation tool but evolved into a broader financial intelligence platform.

The long-term goal is to provide a financial MCP (Model Context Protocol) capable of understanding personal finances, answering historical questions, generating forecasts, detecting spending patterns, and helping make better financial decisions.

---

# Vision

This is NOT a traditional accounting system.

The objective is to create a personal financial knowledge base that combines:

* Bank account movements
* Credit card transactions
* Manual expense tracking
* Fixed expenses
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

Financial movements are considered the source of truth.

Manual records are used to:

* Improve categorization
* Validate expenses
* Detect missing records
* Enrich financial information

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

## Manual Expense Records

Imported from personal Excel spreadsheets.

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

Both represent the same expense.

---

## Fixed Expenses

Fixed expenses represent future planning.

Examples:

* Internet
* Insurance
* Electricity
* Gas
* Mobile Phone

The date when a fixed expense is registered does not necessarily represent the payment date.

Fixed expenses are managed independently from reconciliation.

---

# Reconciliation Model

The reconciliation engine supports:

* 1 ↔ 1
* N ↔ 1
* 1 ↔ N
* N ↔ M

Examples:

## 1 ↔ 1

Excel:

Pharmacy $50.000

Bank:

FARMACIA AMANCAY $49.974

---

## N ↔ 1

Excel:

Pharmacy $20.000

Pharmacy $30.000

Bank:

FARMACIA AMANCAY $50.000

---

## 1 ↔ N

Excel:

Supermarket $100.000

Bank:

Purchase A $60.000

Purchase B $40.000

---

## N ↔ M

Multiple records grouped together.

---

# Movement States

Every financial movement eventually falls into one of these states:

## Pending

Not reviewed yet.

---

## Matched

Successfully reconciled against another source.

---

## Reviewed

Manually reviewed and intentionally left without a counterpart.

Examples:

* Personal transfers
* Gifts
* Internal transfers
* Bank fees
* Interests

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

## Phase 1 - Reconciliation Stabilization

Goals:

* Finish N↔M reconciliation support
* Complete reconciliation UI
* Implement Reviewed movements
* Improve reconciliation workflows
* Improve pending movement management

---

## Phase 2 - Smart Matching

Goals:

* Flexible date matching
* Flexible amount matching
* Description similarity matching
* Automatic suggestions

---

## Phase 3 - Fixed Expenses

Goals:

* Fixed expense tracking
* Automatic payment detection
* Fixed expense analytics

---

## Phase 4 - Financial MCP

Goals:

* Historical financial analysis
* Budget recommendations
* Spending forecasts
* Anomaly detection
* Personalized financial advice

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

1. Read this README.
2. Review the reconciliation workflow.
3. Preserve the existing architecture.
4. Avoid bypassing application services.
5. Keep business rules inside Domain/Application layers.
6. Use the roadmap and business rules as the source of truth.

This document should be considered the starting context for both developers and AI assistants working on the project.
