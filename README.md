# Erp.Api — Damietta Woodworks ERP

Backend for **Damietta Woodworks**, a furniture factory in Damietta that sells wholesale to showrooms (المعارض) across Egypt. It covers the catalog, warehouses and stock, purchasing, sales orders with discount approval, shipping with company and outside trucks, invoicing with credit notes, online card payments through Paymob with automatic refunds, a one-click "confirm and pay" checkout, WhatsApp notifications, and a small HR module.

The frontend is a separate Angular app (`erp.app`).

---

## Contents

1. [Tech stack](#1-tech-stack)
2. [Running it](#2-running-it)
3. [Architecture](#3-architecture)
4. [Security model](#4-security-model)
5. [Data model](#5-data-model)
6. [Modules](#6-modules)
   - [Catalog](#61-catalog)
   - [Inventory](#62-inventory)
   - [Purchasing](#63-purchasing)
   - [Sales orders](#64-sales-orders)
   - [Shipping](#65-shipping)
   - [Invoicing](#66-invoicing)
   - [Online payments](#67-online-payments)
   - [Checkout: confirm and pay](#68-checkout-confirm-and-pay)
   - [Notifications](#69-notifications)
   - [HR](#610-hr)
   - [Identity](#611-identity)
7. [API reference](#7-api-reference)
8. [Errors](#8-errors)
9. [Configuration](#9-configuration)
10. [Testing](#10-testing)
11. [CI guards](#11-ci-guards)
12. [Known limits and backlog](#12-known-limits-and-backlog)

---

## 1. Tech stack

| Area | Choice |
|---|---|
| Runtime | .NET 10, ASP.NET Core controllers |
| Database | PostgreSQL 17, EF Core 10 with Npgsql |
| Auth | ASP.NET Identity users, JWT access tokens, rotating refresh tokens |
| Logging | Serilog, request logging, correlation id per request |
| PDF | QuestPDF (Community license) |
| Payments | Paymob Intention API + HMAC-signed webhooks, void/refund API |
| WhatsApp | OpenWA (self-hosted gateway), behind a provider-neutral interface |
| Background work | One generic `QueueWorker<T>` per queue, `FOR UPDATE SKIP LOCKED` |
| Tests | xUnit, Testcontainers (real PostgreSQL), one shared `ApiFactory` |
| Packaging | Docker, `compose.yaml` (API + Postgres) |

---

## 2. Running it

### With Docker Compose

Create `.env` next to `compose.yaml` (never commit it):

```env
DB_PASSWORD=...
JWT_KEY=...            # at least 32 random characters
ADMIN_PASSWORD=...
ADMIN_EMAIL=admin@company.com
SEED_DEMO_DATA=false
```

```bash
docker compose up --build
```

| Service | URL |
|---|---|
| API | `http://localhost:5013` |
| Swagger (non-production only) | `http://localhost:5013/swagger` |
| Liveness | `GET /health/live` |
| Readiness (checks the DB) | `GET /health/ready` |
| Postgres | `127.0.0.1:5433` |

On start the API **applies migrations**, ensures the **system roles** exist, and creates the **admin user** from `Seed:AdminEmail` / `Seed:AdminPassword`.

### Locally

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5433;Database=erp_db;Username=erp;Password=..." --project src/ErpDashboard.Api
dotnet user-secrets set "Jwt:Key" "..." --project src/ErpDashboard.Api
dotnet run --project src/ErpDashboard.Api
```

Payment keys and the WhatsApp gateway key also live in user-secrets locally and in environment variables in production. They never go in `appsettings*.json` or in a commit. See [Configuration](#9-configuration).

### WhatsApp locally (optional)

Without it, notifications are stored as `Skipped` and the portal still shows every message.

1. Run OpenWA from its own repository (see its README) and read its API key from the container.
2. Open its dashboard, create a session named `erp`, and scan the QR code **with a number used only by this system**.
3. Point the API at it:

```bash
dotnet user-secrets set "Messaging:WhatsApp:Provider" "openwa" --project src/ErpDashboard.Api
dotnet user-secrets set "Messaging:WhatsApp:OpenWa:BaseUrl" "http://localhost:2785" --project src/ErpDashboard.Api
dotnet user-secrets set "Messaging:WhatsApp:OpenWa:SessionId" "erp" --project src/ErpDashboard.Api
dotnet user-secrets set "Messaging:WhatsApp:OpenWa:ApiKey" "..." --project src/ErpDashboard.Api
```

### Migrations and tests

```bash
dotnet ef migrations add <Name> --project src/ErpDashboard.Api
dotnet test ErpDashboard.slnx          # needs Docker for Testcontainers
```

---

## 3. Architecture

### Solution layout

```
src/ErpDashboard.Api
├── Auth/                      login, refresh, logout, JWT
├── Common/                    errors, paging, sorting, Money, BusinessClock, permissions, QueueWorker
├── Controllers/               thin HTTP layer, one per resource
├── Data/
│   ├── Entities/ Enums/ Configurations/
│   ├── Seed/                  roles, permissions, fixed ids, DbSeeder
│   └── AppDbContext.cs        named query filters, soft delete
├── Features/                  business logic, grouped by domain
│   ├── Finance/Invoices       invoices, credit notes, document numbers, PDF
│   ├── Hr/                    departments, employees
│   ├── Identity/              users, roles
│   ├── Inventory/             products, categories, materials, warehouses, stock
│   ├── Notifications/         WhatsApp outbox, checkout messages (provider-neutral)
│   ├── Payments/              online payments, webhook processing, refunds (provider-neutral)
│   ├── Purchasing/            suppliers, purchase orders
│   ├── Sales/                 showrooms, orders, checkout
│   └── Shipping/              carriers, rates, quotes, shipments
├── Infrastructure/
│   ├── Payments/Paymob        Paymob client + webhook verifier (only place that names the provider)
│   └── Messaging/OpenWa       WhatsApp sender (only place that names the gateway)
├── Middlewares/               correlation id
├── Migrations/
└── Settings/                  typed options
tests/
├── ErpDashboard.Api.UnitTests
└── ErpDashboard.Api.IntegrationTests
```

### Request pipeline

```mermaid
flowchart LR
    C[Client] --> CID[CorrelationId middleware]
    CID --> LOG[Serilog request log]
    LOG --> EX[Exception handler<br/>ProblemDetails]
    EX --> AUTHN[JWT authentication]
    AUTHN --> AUTHZ[Permission check<br/>HasPermission]
    AUTHZ --> CTRL[Controller]
    CTRL --> SVC[Feature service]
    SVC --> DB[(PostgreSQL)]
    SVC -. HTTP .-> PM[Paymob]
    WH[Paymob webhook] --> INTAKE[Webhook intake<br/>anonymous + HMAC]
    INTAKE --> DB
    subgraph Workers[QueueWorker&lt;T&gt; background services]
      W1[PaymentWebhookProcessor]
      W2[CheckoutExpiryProcessor]
      W3[RefundProcessor]
      W4[NotificationProcessor]
    end
    Workers --> DB
    W3 -. HTTP .-> PM
    W4 -. HTTP .-> WA[OpenWA]
```

### Background work

Every queue is a processor with one method, `ProcessNextAsync`, run by the same generic `QueueWorker<T>` (idle wait, error isolation, `Enabled` / `PollSeconds` per queue). Adding a queue is one line: `AddQueueWorker<TProcessor>(configuration, "<section>")`.

| Queue | Claims | Pattern |
|---|---|---|
| Payment webhooks | `Received` events that are due | Row lock + savepoint; failure → backoff, `Failed` after 8 |
| Checkout expiry | `AwaitingPayment` checkouts past 30 minutes | Row lock + savepoint; failure → retry in 1 minute without blocking others |
| Refunds | `NeedsRefund` attempts that are due | **Lease**: short committed claim, HTTP outside any transaction |
| Notifications | `Pending` WhatsApp messages that are due | **Lease**, same as refunds |

All claims use `FOR UPDATE SKIP LOCKED`, so several API instances can run the same worker safely.

### Cross-cutting rules

| Rule | How it's enforced |
|---|---|
| **Business dates are Cairo dates**, not UTC dates | `IBusinessClock` (`Today`, `StartOfDayUtc`), backed by `TimeProvider` and `Company:TimeZone = Africa/Cairo`. It handles the DST midnight gap. A CI guard blocks `DateOnly.FromDateTime(DateTime.…)`. |
| **Money has at most 2 decimals** | Validated on every money input. `Money.ToMinorUnits` throws on more. Rounding is `AwayFromZero`, per line. |
| **Stock changes go through one service** | `IStockService` is the only writer of `WarehouseStocks` and `StockMovements`. Enforced by a CI guard. |
| **Errors are ProblemDetails** | `GlobalExceptionHandler` maps exceptions to 400/404/409/428/499/504 and adds a `code` where the client must branch. See [Errors](#8-errors). |
| **No transaction open during HTTP calls** | Payment attempts are saved before the Paymob call and updated after it. Refunds and WhatsApp sends take a short lease first, then call out with no transaction open. |
| **Messages are written with the change they describe** | A WhatsApp notification is inserted in the same transaction as the business change (outbox), so a rolled-back change never sends a message. |
| **Responses are JSON** | The string output formatter is removed, so Swagger and clients default to `application/json`; validation errors stay `application/problem+json`. |
| **Every request has a correlation id** | Returned in `X-Correlation-ID` and in every error body. |
| **Sensitive data is never logged** | No JWTs, passwords, payment keys, checkout URLs or webhook bodies in logs. CI blocks `EnableSensitiveDataLogging`, HTTP logging and `{@…}` destructuring. |

---

## 4. Security model

### Authentication

| Item | Value |
|---|---|
| Access token | JWT, 15 minutes |
| Refresh token | 7 days, **rotated on every use** |
| Reuse detection | A rotated token used again after a short grace period revokes **all** of that user's sessions |
| Logout | `POST /api/auth/logout` (this session), `POST /api/auth/revoke-all-sessions` (all mine), `POST /api/users/{id}/logout-all` (admin) |

### Authorization: permissions, not roles

Every endpoint carries `[HasPermission("<code>")]`. Roles are just named sets of permission codes stored in `Roles.Permissions` (`text[]`).

- **Admin** always has every code. `DbSeeder` re-syncs it on each start.
- **Other system roles** get new codes through a migration (`array_append`), so roles edited in production are not overwritten.
- **Custom roles** can be created through `/api/roles`.

Two tests keep this honest:
- `EndpointPermissionTests` fails if any `api/` endpoint has no permission.
- `AnonymousEndpoints_AreOnlyTheKnownOnes` fails if a new `[AllowAnonymous]` appears without being listed on purpose.

### Role matrix

| Permission | Admin | Manager | Inventory Mgr | Accountant | Sales | Showroom User | User |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| users.* / roles.* | ✓ | | | | | | |
| showrooms.read | ✓ | ✓ | | ✓ | ✓ | | ✓ |
| showrooms.write | ✓ | ✓ | | | | | |
| showrooms.access-all | ✓ | ✓ | ✓ | ✓ | ✓ | | ✓ |
| orders.checkout | ✓ | | | | ✓ | ✓ | |
| products.read | ✓ | ✓ | ✓ | | ✓ | | ✓ |
| products.write / adjust-stock | ✓ | | ✓ | | | | |
| products.view-cost | ✓ | ✓ | ✓ | | | | |
| categories.read | ✓ | ✓ | ✓ | | ✓ | | ✓ |
| categories.write | ✓ | | ✓ | | | | |
| suppliers.read | ✓ | ✓ | ✓ | | | | |
| suppliers.write | ✓ | | ✓ | | | | |
| purchase-orders.* | ✓ | ✓ | ✓ | | | | |
| warehouses.read | ✓ | ✓ | ✓ | | ✓ | | |
| warehouses.write | ✓ | | ✓ | | | | |
| warehouses.access-all | ✓ | | | | | | |
| stock.reconcile | ✓ | | | | | | |
| orders.read | ✓ | ✓ | | ✓ | ✓ | ✓ | ✓ |
| orders.create | ✓ | ✓ | | | ✓ | | |
| orders.write / update-status | ✓ | ✓ | | | | | |
| orders.approve-discount | ✓ | ✓ | | | | | |
| invoices.read | ✓ | ✓ | | ✓ | | ✓ | |
| invoices.write / pay | ✓ | | | ✓ | | | |
| invoices.pay-online | ✓ | | | | | ✓ | |
| carriers.read | ✓ | ✓ | ✓ | | | | |
| carriers.write | ✓ | ✓ | | | | | |
| shipping-quotes.read | ✓ | ✓ | ✓ | | ✓ | ✓ | |
| shipments.read | ✓ | ✓ | ✓ | ✓ | ✓ | | |
| shipments.book | ✓ | ✓ | ✓ | | | | |
| payments.webhooks.read | ✓ | | | | | | |
| departments.read / employees.read | ✓ | ✓ | | ✓ | | | |
| departments.write / employees.write | ✓ | | | | | | |
| employees.view-salary | ✓ | | | ✓ | | | |

### Data scope (row-level)

On top of permissions, EF Core **named query filters** limit which rows a user can see:

| Filter | Applies to | Rule |
|---|---|---|
| `QueryFilters.Showroom` | Orders, Invoices, Showrooms | Users with `showrooms.access-all` see everything. A showroom user sees only rows of **their own showroom**. Anyone else (and background workers, which have no user) sees nothing. |
| `QueryFilters.SoftDelete` | Products, Showrooms, Employees | Deleted rows are hidden. `SaveChanges` turns a delete into `IsDeleted = true`. |

Background code that must see every row opts out explicitly, for example `IgnoreQueryFilters([QueryFilters.Showroom])` in the payment webhook handler, the checkout completion and `CancelBySystemAsync`.

> **Tests too.** A test that reads `Invoices` or `Orders` from a fresh scope has no user, so the filter hides everything and `Any()` is always false. Assertions on those tables must use `IgnoreQueryFilters([QueryFilters.Showroom])`, or "nothing was created" checks pass for the wrong reason.

`shipping-quotes.read` is separate from `carriers.read` on purpose: showrooms and Sales see carrier names and prices, never the truck owners' phone numbers.

Warehouse access is per user (`UserWarehouses`). `warehouses.access-all` bypasses it.

### The one public endpoint that touches money

`POST /api/payments/webhook` is anonymous. Its HMAC signature is its authentication. See [Online payments](#67-online-payments).

---

## 5. Data model

### Core entities

```mermaid
erDiagram
    SHOWROOM ||--o{ ORDER : places
    SHOWROOM ||--o{ INVOICE : billed
    SHOWROOM ||--o{ USER : "showroom users"
    WAREHOUSE ||--o{ ORDER : "ships from"
    WAREHOUSE ||--o{ WAREHOUSE_STOCK : holds
    PRODUCT ||--o{ WAREHOUSE_STOCK : stocked
    PRODUCT ||--o{ STOCK_MOVEMENT : ledger
    WAREHOUSE ||--o{ STOCK_MOVEMENT : ledger
    CATEGORY ||--o{ PRODUCT : groups
    MATERIAL ||--o{ PRODUCT : "made of"
    PRODUCT ||--o{ PRODUCT_PRICE_HISTORY : prices

    ORDER ||--|{ ORDER_LINE : contains
    ORDER ||--o{ ORDER_STATUS_CHANGE : history
    ORDER ||--o| SHIPMENT : "shipped by"
    SHIPMENT ||--|{ SHIPMENT_VEHICLE : uses
    CARRIER ||--o{ SHIPMENT_VEHICLE : drives
    CARRIER ||--o{ CARRIER_RATE : "price per governorate"

    ORDER ||--o| INVOICE : "invoiced as"
    INVOICE ||--|{ INVOICE_LINE : contains
    INVOICE ||--o{ INVOICE_PAYMENT : paid
    INVOICE ||--o{ CREDIT_NOTE : credited
    CREDIT_NOTE ||--|{ CREDIT_NOTE_LINE : contains
    INVOICE ||--o{ PAYMENT_ATTEMPT : "online tries"
    ORDER ||--o{ PAYMENT_ATTEMPT : "checkout tries"
    PAYMENT_ATTEMPT ||--o| INVOICE_PAYMENT : "records at most one"

    ORDER ||--o{ ORDER_CHECKOUT : "confirm and pay"
    ORDER_CHECKOUT ||--|{ ORDER_CHECKOUT_VEHICLE : "chosen vehicles"
    ORDER_CHECKOUT ||--o| PAYMENT_ATTEMPT : "paid by"
    ORDER_CHECKOUT ||--o{ NOTIFICATION : "WhatsApp outbox"

    SUPPLIER ||--o{ PURCHASE_ORDER : supplies
    PURCHASE_ORDER ||--|{ PURCHASE_LINE : contains

    ROLE ||--o{ USER : grants
    USER ||--o{ USER_WAREHOUSE : "may use"
    DEPARTMENT ||--o{ EMPLOYEE : employs
```

`PaymentWebhookEvent`, `StockTransfer` and `DocumentNumberCounter` stand on their own and are described in their modules.

### Conventions

| Convention | Where | Why |
|---|---|---|
| **Snapshots** | `OrderLine.ProductName/Sku/UnitPrice`, `ShipmentVehicle.CarrierName/Price`, `Shipment.Governorate`, `OrderCheckoutVehicle.CarrierName/Price` | A later rename or price change must not rewrite history, a printed invoice, or what a customer already paid for. |
| **A payment attempt pays exactly one thing** | `PaymentAttempt.InvoiceId` xor `OrderId` (check constraint) | Invoice payments (ERP-16) and order checkouts (ERP-18) share the same gateway code and webhook pipeline. |
| **Optimistic concurrency with `xmin`** | `Order.Version` | The client sends it in `If-Match`. A missing header is 428, a stale one is 409 `order_changed`. |
| **Gapless document numbers** | Invoices `INV-yyyy-000001`, credit notes `CN-…`, shipments `SHP-…` | One counter row per series and year, incremented with `INSERT … ON CONFLICT … RETURNING` **inside** the business transaction. A rollback gives the number back. |
| **Enums stored as text** | Statuses, governorates | Safe to reorder. Exception: `PaymentMethod` is stored as an int, so new values go at the end only. |
| **Soft delete** | Products, Showrooms, Employees | Referenced by history. A CI guard blocks `ExecuteDelete` on them. |
| **Guid keys set in code** | Most entities (`ValueGeneratedNever`) | Add such entities through their `DbSet`, not through a navigation, or EF may treat them as modified rows. |
| **No `SELECT *` into `Order`** | Raw SQL on `Orders` | `Order.Version` is the `xmin` system column, which `SELECT *` does not return. Lock with `SELECT 1 … FOR UPDATE`, then read with LINQ. |

---

## 6. Modules

### 6.1 Catalog

- **Products** have SKU (normalized, unique, duplicate → 409), price, cost, reorder level, size (cm), weight (kg) and a **material** (a lookup table rather than a fixed wood type).
- **Cost price** is returned only to users with `products.view-cost`.
- **Price history**: every price change is recorded (`GET /api/products/{id}/price-history`).
- Products are soft-deleted.

### 6.2 Inventory

Stock is a **ledger**. `StockMovements` is the source of truth, and `WarehouseStocks.Quantity` is the running balance kept in step with it.

```mermaid
flowchart TB
    subgraph Ledger
      M[(StockMovements)]
    end
    subgraph Balance
      WS[(WarehouseStocks<br/>Quantity, Reserved)]
    end
    PO[Purchase order received] -->|PurchaseReceipt +| S
    ADJ[Manual adjustment] -->|Adjustment ±| S
    OPEN[Opening balance] -->|OpeningBalance +| S
    TR1[Transfer dispatched] -->|TransferOut −| S
    TR2[Transfer received] -->|TransferIn +| S
    TR3[Transfer cancelled] -->|TransferReturn +| S
    ORD1[Order created] -->|reserve| S
    ORD2[Order shipped] -->|SalesIssue − and unreserve| S
    ORD3[Order cancelled] -->|release reservation| S
    S[IStockService<br/>TryApply / TryReserve / Release / IssueReserved] --> M
    S --> WS
```

| Concept | Rule |
|---|---|
| **Available** | `Quantity − Reserved`. Orders reserve stock **at creation**, so two orders cannot sell the same chair. |
| **Shortage** | 409 with a structured body (product, requested, available), never a silent negative. |
| **Transfers** | Two steps: dispatch (`InTransit`, stock leaves A) → receive (stock enters B), or cancel (stock returns to A). Rows are locked in a stable order to avoid deadlocks. |
| **Reconciliation** | `GET /api/stock/reconciliation` compares each balance with the sum of its ledger, and `Reserved` with open orders (Draft, PendingApproval, Confirmed, Preparing). |
| **Low stock** | `GET /api/stock/low`: available at or below the product's reorder level, per warehouse. |
| **Movement history** | `GET /api/warehouses/{id}/movements`, with a running balance. |

All stock methods require an open transaction. Tests inject failures (`StockFaults`) to prove that partial work rolls back.

### 6.3 Purchasing

Suppliers and purchase orders. A purchase order is `Pending` → `Received` (stock enters through the ledger as `PurchaseReceipt`) or `Cancelled`.

### 6.4 Sales orders

```mermaid
stateDiagram-v2
    [*] --> Draft: create (reserves stock)
    Draft --> PendingApproval: submit, any line discount over 10%
    Draft --> Confirmed: submit
    PendingApproval --> Confirmed: approve
    PendingApproval --> Cancelled: reject (releases stock)
    Confirmed --> Preparing
    Preparing --> Shipped: needs a booked shipment<br/>issues reserved stock
    Shipped --> Delivered
    Draft --> Cancelled
    Confirmed --> Cancelled
    Preparing --> Cancelled: releases stock<br/>cancels the shipment
    Delivered --> [*]
    Cancelled --> [*]
```

| Rule | Detail |
|---|---|
| **Discount approval** | Threshold `Sales:DiscountApprovalThresholdPercent` (default 10). Approve/reject needs `orders.approve-discount`. |
| **Editing** | Only Draft orders can be edited: payment method, shipping address and **governorate** (one of Egypt's 27). |
| **Concurrency** | Every state change needs `If-Match: "<version>"`. Responses carry the new version. |
| **Status changes** | `PATCH /status` handles Preparing / Shipped / Delivered. Submit, approve, reject and cancel have their own endpoints. |
| **Invalid moves** | 409 `invalid_transition` with `currentStatus` and `allowedTransitions`. |
| **History** | Every change is stored in `OrderStatusChanges`, with who, when and a reason. |
| **Who can see** | Showroom users see only their showroom's orders (query filter). |
| **Checkout entry** | `POST /orders/{id}/checkout` submits a Draft through the same `SubmitAsync`. If a discount needs approval the order stops at PendingApproval and checkout returns `AwaitingApproval`; it is started again after approval. See 6.8. |
| **System cancel** | `CancelBySystemAsync` cancels without a user (checkout expiry, failed completion). Same rules as a manual cancel: releases stock, cancels the shipment, writes history with a system reason. |

### 6.5 Shipping

Furniture is moved by **the company's own trucks** and by **known outside truck owners**. Both are rows in one `Carriers` table.

| Concept | Rule |
|---|---|
| **Carrier** | Name (unique), phone (required for outside carriers), active flag. Never deleted, only deactivated. |
| **Company trucks** | One fixed row (`CarrierIds.CompanyFleet`) created by the migration. It has no special area rule: it serves wherever it has a price. |
| **Rate** | One **price per vehicle per governorate**. Weight does not matter: a trip is a trip, even with an empty truck. |
| **Quote** | `GET /api/shipping-quotes?governorate=Giza` lists every active carrier with price, availability and the reason if unavailable, and returns `cheapestCarrierId`. Ties prefer company trucks, then name. |
| **Shipment** | One per order. It can use **several vehicles from different carriers**. Each vehicle stores its carrier name and price at booking time. Tracking number `SHP-yyyy-000001`. |
| **Booking** | `POST /api/orders/{id}/shipment` for Confirmed or Preparing orders that have a governorate. It locks the order row, so booking, cancelling and shipping cannot interleave. |
| **Awaiting list** | `GET /api/shipments/awaiting`: orders ready to ship, without prices, for the Inventory Manager (who cannot open orders themselves). |
| **Order link** | `Shipped` requires a booked shipment (`shipment_required`). Cancelling the order cancels its shipment. |

```mermaid
sequenceDiagram
    participant IM as Inventory Manager
    participant API
    participant DB
    IM->>API: GET /shipments/awaiting
    IM->>API: GET /shipping-quotes?governorate=Dakahlia
    IM->>API: POST /orders/{id}/shipment {vehicles:[A, B]}
    API->>DB: BEGIN, lock order row
    API->>DB: check status, governorate, no shipment yet
    API->>DB: price each vehicle from CarrierRates
    API->>DB: next SHP number, insert shipment + vehicles
    API->>DB: COMMIT
    API-->>IM: 201 SHP-2026-000123, total = A + B
```

### 6.6 Invoicing

| Concept | Rule |
|---|---|
| **Sources** | Manual (Draft → Issue) or one click from a Shipped/Delivered order (`POST /api/invoices/from-order/{orderId}`). |
| **Lines** | Line discount, then tax on the discounted amount. Tax and totals are rounded per line. Default tax 14%. |
| **Shipping lines** | From an order, each shipment vehicle becomes its own line `Shipping – {carrier} ({SHP-…})` at **0% tax**, priced at booking time. |
| **Numbers** | `INV-yyyy-000001`, gapless, taken only when the invoice is issued. |
| **Payments** | Partial payments allowed, never above the balance. Manual methods: Cash, CreditCard (counter). `Online` is written only by the payment processor. |
| **Payment status** | Computed, not stored: `Due`, `PartiallyPaid`, `Paid`, `Overdue` (Overdue wins when the due date has passed with a balance). |
| **Credit notes** | Instead of cancelling an issued invoice. Per invoice line, capped at that line's remaining net. Numbered `CN-…`. The balance may go negative (refund due). |
| **PDF** | `GET /api/invoices/{id}/pdf`. |

```mermaid
flowchart LR
    D[Draft] -->|issue: takes INV number| I[Issued]
    D -->|delete| X[gone]
    I -->|payments| B{balance}
    I -->|credit notes| B
    B -->|> 0, before due| Due
    B -->|> 0, some paid| PartiallyPaid
    B -->|> 0, after due| Overdue
    B -->|<= 0| Paid
```

### 6.7 Online payments

A `PaymentAttempt` pays **one thing**: an issued invoice (this section) or an order checkout (6.8). A check constraint enforces exactly one of the two. **Money is recorded only from a signed webhook, never from the redirect back to the site**, because anyone can open a URL.

```mermaid
sequenceDiagram
    autonumber
    actor SU as Showroom user
    participant API
    participant PM as Paymob
    participant W as Webhook worker
    participant DB

    SU->>API: POST /invoices/{id}/online-payments
    API->>DB: save PaymentAttempt (Pending), reuse one from the last 30 min
    API->>PM: create intention (amount in piasters, attempt id)
    PM-->>API: client secret
    API-->>SU: checkoutUrl (Paymob slow → 504)
    SU->>PM: pays with card on Paymob's page
    PM->>API: POST /payments/webhook?hmac=…
    API->>API: verify HMAC-SHA512 (constant time)
    API->>DB: store PaymentWebhookEvent (Received or Rejected)
    API-->>PM: 200 valid / 401 forged, in under 1 s
    loop every 5 s
        W->>DB: claim one event FOR UPDATE SKIP LOCKED
        W->>DB: lock invoice, decide, write (one transaction)
    end
```

**What the worker writes**

| What happened | Attempt | Invoice | Event |
|---|---|---|---|
| Paid, amount fits the balance | Succeeded | + `Online` payment | Processed |
| Paid for a checkout | Succeeded, or NeedsRefund if completion fails for a business reason | see 6.8 | Processed |
| Card declined | Failed | – | Processed |
| Paid more than due, invoice cancelled, or wrong amount/currency | NeedsRefund | – | Processed |
| Same callback again | no change | – | Ignored (duplicate) |
| Pending, refund or void callback | no change | – | Ignored |
| Money for an unknown attempt | – | – | NeedsReview |
| Error while processing | no change | rolled back | retry with backoff, Failed after 8 tries |

**Guarantees**

- **Paid once**: `InvoicePayment.PaymentAttemptId` is unique, and the invoice row is locked like a manual payment.
- **Retry without double work**: the handler runs inside a savepoint. A failure rolls back its writes but keeps the event lock.
- **The API never stops for a bad webhook**: the worker catches everything per iteration.
- **Provider-neutral core**: `IPaymentGateway` and `IPaymentWebhookVerifier` live in `Features/Payments`. Paymob code lives only in `Infrastructure/Payments/Paymob`, and CI enforces it.
- **Admin view**: `GET /api/payments/webhook-events` (filters: status, signature, invoice, attempt, dates, transaction id). The details view **redacts** names, emails, phones and tokens in the stored payload.

Verified end to end in the Paymob sandbox: success, decline, and a cash payment made while the customer was on the checkout page.

**Automatic void or refund**

Every `NeedsRefund` attempt is returned to the customer by `RefundProcessor`, with no human step.

```mermaid
flowchart LR
    NR[NeedsRefund] --> L[lease 2 min<br/>short committed claim]
    L --> S{paid today<br/>Cairo time?}
    S -->|yes| V[void]
    V -->|ok| VD[Voided]
    V -->|rejected| R[refund]
    S -->|no| R
    R -->|ok| RF[Refunded]
    R -->|declined| RFF[RefundFailed]
    L -->|provider down| RT[retry with backoff]
    RT -->|8 tries| RFF
    VD --> M[queue WhatsApp message]
    RF --> M
    RFF --> M
```

| Rule | Detail |
|---|---|
| **No HTTP inside a transaction** | The attempt is leased in a short transaction; the Paymob call runs outside it; the result is written in a second short transaction. |
| **Void first** | Same Cairo day → `void_refund/void` (no fee, nothing shows on the card). Otherwise, or if void is rejected → `void_refund/refund`. Both use `Authorization: Token {SecretKey}`. |
| **No transaction id** | Goes straight to RefundFailed: nothing can be reversed automatically. |
| **Outcome message** | Voided: "nothing was charged". Refunded: refund on its way. RefundFailed: "our team will contact you" and the attempt stays visible for a human. |
| **Refund/void callbacks** | Paymob also sends webhooks for these. They are stored and ignored; the processor trusts the API response. |

### 6.8 Checkout: confirm and pay

The customer (portal) or Sales confirms an order, chooses the trucks, and pays the **order** online. The invoice is issued only after the money arrives.

```mermaid
sequenceDiagram
    autonumber
    actor C as Portal user / Sales
    participant API
    participant PM as Paymob
    participant W as Webhook worker
    participant X as Expiry worker
    participant DB

    C->>API: GET /shipping-quotes?governorate=…
    C->>API: POST /orders/{id}/checkout {vehicles} + If-Match
    API->>DB: Draft? submit (may stop at AwaitingApproval)
    API->>DB: snapshot vehicle prices, exact invoice math<br/>OrderCheckout AwaitingPayment, expires in 30 min
    API->>PM: intention for the order attempt
    API-->>C: checkoutUrl
    C->>PM: pays
    PM->>API: signed webhook
    W->>DB: lock checkout row
    alt still AwaitingPayment
        W->>DB: one transaction: book shipment, issue invoice,<br/>record Online payment, checkout Completed
    else business failure
        W->>DB: cancel order (system), attempt NeedsRefund, checkout Failed
    end
    X->>DB: lock same checkout row after 30 min unpaid
    X->>DB: cancel order, release stock, checkout Expired
```

| Decision | Choice |
|---|---|
| **What is paid** | The order, not an invoice. The amount is computed with the invoice code itself (`InvoiceFactory.BuildLines` / `CalculateTotal`), so the invoice issued later matches to the piaster. |
| **Who starts it** | Portal user for their showroom, or Sales (who gets a link to send). Permission `orders.checkout`. |
| **Drafts** | Submitted through the normal submit. A discount above the threshold returns `AwaitingApproval` and no payment link. |
| **Vehicles** | The user chooses carriers and vehicles from the quote; prices are snapshotted in `OrderCheckoutVehicles`. |
| **Allowed orders** | Draft or Confirmed (else 409 `not_checkoutable`), with a governorate (else `governorate_missing`). |
| **One open checkout** | A second start while one is AwaitingPayment → 409 `checkout_in_progress`. |
| **Payment window** | 30 minutes. Unpaid → order cancelled by the system, stock released, checkout Expired. |
| **Race** | Payment and expiry lock the same checkout row. Whoever comes second sees the final status. Money that arrives after expiry → NeedsRefund. |

**When completion fails**

| Failure | What happens |
|---|---|
| **Business** (stock gone, order no longer valid, booking refused) | Compensate: `CancelBySystemAsync`, attempt NeedsRefund (refund runs automatically), checkout Failed with a `CustomerMessage` for the portal. |
| **Technical** (database error, timeout) | Rolled back in a savepoint, webhook retried with backoff like any other event. |
| **Expiry worker error** | Retried after 1 minute; the checkout stays AwaitingPayment until it succeeds. |

`GET /orders/{id}/checkout` returns the latest checkout: status, goods, shipping and total amounts, vehicles, payment URL while open, expiry, and `CustomerMessage`.

### 6.9 Notifications (WhatsApp)

Showrooms may not have email, but they all have WhatsApp. Messages go through an **outbox**.

```mermaid
flowchart LR
    CN[ICheckoutNotifier<br/>only writer of CustomerMessage] -->|same transaction| N[(Notifications<br/>Pending)]
    N --> P[NotificationProcessor<br/>lease 2 min]
    P --> S[IWhatsAppSender]
    S --> O[OpenWA]
    P -->|ok| Sent
    P -->|landline / no number| Skipped
    P -->|8 tries| Failed
```

| Rule | Detail |
|---|---|
| **Outbox** | The message row is written in the same transaction as the business change, so a rollback never sends a message and a commit never loses one. |
| **Single writer** | `ICheckoutNotifier` sets `CustomerMessage` and queues the WhatsApp message together, so portal and phone say the same thing. |
| **What goes to WhatsApp** | Checkout completed (shipment + invoice numbers), checkout expired, refund outcome (Voided / Refunded / RefundFailed). The compensation message ("order cancelled, refund on its way") is portal-only; the refund outcome follows on WhatsApp. |
| **Recipient** | The order's showroom phone. Egyptian mobiles normalized to `201xxxxxxxxx`. Landlines and invalid numbers → Skipped at queue time, never sent. |
| **Provider-neutral** | `IWhatsAppSender` in `Features/Notifications`; OpenWA only in `Infrastructure/Messaging/OpenWa` (CI enforces). Empty `Messaging:WhatsApp:Provider` → messages Skipped. |
| **Risk** | OpenWA drives WhatsApp Web, which is unofficial. Use a dedicated number; it can be banned. Swapping to the official Cloud API means one new `IWhatsAppSender`. |

### 6.10 HR

Departments and employees, soft-deleted. Salary is returned only with `employees.view-salary`.

### 6.11 Identity

- Users have one role, an optional showroom (for showroom users) and a list of warehouses they may use.
- Roles are editable sets of permission codes. System roles cannot be deleted or renamed, and Admin's permissions cannot be edited.
- `GET /api/profile/me` returns the current user and their role.

---

## 7. API reference

All routes are under `/api`. Every non-anonymous route needs `Authorization: Bearer <jwt>`.

### Auth and profile

| Method | Route | Permission |
|---|---|---|
| POST | `/auth/login` | anonymous |
| POST | `/auth/refresh` | anonymous |
| POST | `/auth/logout` | signed in |
| POST | `/auth/revoke-all-sessions` | signed in |
| GET | `/profile/me` | signed in |

### Identity

| Method | Route | Permission |
|---|---|---|
| GET | `/users`, `/users/{id}` | users.read |
| POST / PUT / DELETE | `/users`, `/users/{id}` | users.write |
| POST | `/users/{id}/logout-all` | users.write |
| GET | `/users/{id}/warehouses` | users.read |
| PUT | `/users/{id}/warehouses` | users.write |
| GET | `/roles`, `/roles/{id}`, `/roles/permissions` | roles.read |
| POST / PUT / DELETE | `/roles`, `/roles/{id}` | roles.write |

### Catalog and inventory

| Method | Route | Permission |
|---|---|---|
| GET | `/products`, `/products/{id}`, `/products/{id}/price-history` | products.read |
| POST / PUT / DELETE | `/products`, `/products/{id}` | products.write |
| GET | `/categories`, `/categories/{id}` | categories.read |
| POST / PUT / DELETE | `/categories`, `/categories/{id}` | categories.write |
| GET | `/materials` | products.read |
| POST / PUT | `/materials`, `/materials/{id}` | products.write |
| GET | `/warehouses`, `/warehouses/{id}` | warehouses.read |
| POST / PUT | `/warehouses`, `/warehouses/{id}` | warehouses.write |
| GET | `/warehouses/{id}/stock` | products.read |
| POST | `/warehouses/{id}/stock/adjust` | products.adjust-stock |
| GET | `/warehouses/{id}/movements` | warehouses.read |
| POST | `/stock/transfers`, `/stock/transfers/{id}/receive`, `/stock/transfers/{id}/cancel` | products.adjust-stock |
| GET | `/stock/transfers`, `/stock/transfers/{id}` | warehouses.read |
| GET | `/stock/low` | products.read |
| GET | `/stock/reconciliation` | stock.reconcile |

### Purchasing

| Method | Route | Permission |
|---|---|---|
| GET | `/suppliers`, `/suppliers/{id}` | suppliers.read |
| POST / PUT / DELETE | `/suppliers`, `/suppliers/{id}` | suppliers.write |
| GET | `/purchaseorders`, `/purchaseorders/{id}` | purchase-orders.read |
| POST / PUT | `/purchaseorders`, `/purchaseorders/{id}` | purchase-orders.write |
| PATCH | `/purchaseorders/{id}/status` | purchase-orders.update-status |

### Sales

| Method | Route | Permission | If-Match |
|---|---|---|:-:|
| GET | `/showrooms`, `/showrooms/{id}` | showrooms.read | |
| POST / PUT / DELETE | `/showrooms`, `/showrooms/{id}` | showrooms.write | |
| GET | `/orders`, `/orders/{id}`, `/orders/{id}/history` | orders.read | |
| POST | `/orders` | orders.create | |
| PUT | `/orders/{id}` | orders.write | ✓ |
| POST | `/orders/{id}/submit` | orders.create | ✓ |
| POST | `/orders/{id}/approve`, `/orders/{id}/reject` | orders.approve-discount | ✓ |
| PATCH | `/orders/{id}/status` | orders.update-status | ✓ |
| POST | `/orders/{id}/cancel` | orders.write | ✓ |
| POST | `/orders/{id}/checkout` | orders.checkout | ✓ |
| GET | `/orders/{id}/checkout` | orders.read | |

### Shipping

| Method | Route | Permission |
|---|---|---|
| GET | `/carriers`, `/carriers/{id}` | carriers.read |
| POST / PUT | `/carriers`, `/carriers/{id}` | carriers.write |
| PUT / DELETE | `/carriers/{id}/rates/{governorate}` | carriers.write |
| GET | `/shipping-quotes?governorate=` | shipping-quotes.read |
| POST | `/orders/{id}/shipment` | shipments.book |
| GET | `/orders/{id}/shipment` | shipments.read |
| GET | `/shipments/awaiting` | shipments.book |

### Finance and payments

| Method | Route | Permission |
|---|---|---|
| GET | `/invoices`, `/invoices/{id}`, `/invoices/{id}/pdf` | invoices.read |
| POST / PUT / DELETE | `/invoices`, `/invoices/{id}` (drafts) | invoices.write |
| PATCH | `/invoices/{id}/issue` | invoices.write |
| POST | `/invoices/from-order/{orderId}` | invoices.write |
| POST | `/invoices/{id}/credit-notes` | invoices.write |
| POST | `/invoices/{id}/payments` | invoices.pay |
| POST | `/invoices/{id}/online-payments` | invoices.pay-online |
| POST | `/payments/webhook?hmac=` | anonymous, HMAC, max 64 KB |
| GET | `/payments/webhook-events`, `/payments/webhook-events/{id}` | payments.webhooks.read |

### HR

| Method | Route | Permission |
|---|---|---|
| GET | `/departments`, `/departments/{id}` | departments.read |
| POST / PUT / DELETE | `/departments`, `/departments/{id}` | departments.write |
| GET | `/employees`, `/employees/{id}` | employees.read |
| POST / PUT / DELETE | `/employees`, `/employees/{id}` | employees.write |

### Lists

List endpoints share `PageQuery`: `page`, `pageSize`, `search`, `sortBy`, `sortDir`. They return `PagedResult<T>` (`items`, `page`, `pageSize`, `total`). Sorting goes through a whitelist (`SortMap`) with a stable tie-breaker.

---

## 8. Errors

Every error is RFC 9110 ProblemDetails with `correlationId`. Some carry a `code` the client should branch on.

| Status | `code` | When |
|---|---|---|
| 400 | – | Validation. `errors` holds field-level messages, for example `vehicles[1].carrierId`. |
| 401 | – | Missing/expired token, bad refresh token, forged webhook |
| 403 | – | Missing permission |
| 404 | – | Not found, **or outside your showroom scope** |
| 409 | `order_changed` | `If-Match` version is stale |
| 409 | `invalid_transition` | Order status move not allowed. Includes `currentStatus` and `allowedTransitions`. |
| 409 | `shipment_required` | Marking an order Shipped without a booked shipment |
| 409 | `not_bookable` | Booking a shipment for an order that is not Confirmed/Preparing |
| 409 | `governorate_missing` | Booking or checkout for an order with no governorate |
| 409 | `already_booked` | The order already has a shipment |
| 409 | `not_checkoutable` | Checkout on an order that is not Draft or Confirmed |
| 409 | `checkout_in_progress` | The order already has a checkout awaiting payment |
| 409 | – | Unique violation (duplicate SKU, carrier name, …), stock shortage (structured body), concurrent update |
| 413 | – | Webhook body over 64 KB |
| 428 | – | `If-Match` missing on an order change |
| 499 | – | The client went away |
| 504 | – | The payment provider did not answer in time |

---

## 9. Configuration

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | – | Secret |
| `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience` | – | Key is secret |
| `Jwt:DurationInMinutes` | 15 | Access token |
| `Jwt:ExpiryDays` | 7 | Refresh token |
| `Seed:AdminEmail`, `Seed:AdminPassword` | – | First admin, secret password |
| `Seed:DemoData` | false | Demo data seeding (backlog) |
| `Cors:AllowedOrigins:0` | – | Angular app origin |
| `Company:TimeZone` | Africa/Cairo | Business dates |
| `Finance:DefaultTaxRate` | 14 | Percent |
| `Finance:DefaultDueDays` | 30 | Invoice due date |
| `Sales:DiscountApprovalThresholdPercent` | 10 | Above this, orders need approval |
| `Payments:Provider` | "" | `paymob` or empty (online payments disabled) |
| `Payments:Paymob:SecretKey`, `PublicKey`, `HmacSecret`, `IntegrationId` | – | **Secrets**: user-secrets locally, environment variables in production |
| `Payments:Paymob:ApiBaseUrl` | https://accept.paymob.com | |
| `Payments:Paymob:CheckoutBaseUrl` | https://eg.checkout.paymob.com | |
| `Payments:Paymob:NotificationUrl`, `RedirectionUrl` | – | Public URL of `/api/payments/webhook` and of the frontend |
| `Payments:Paymob:TimeoutSeconds` | 10 | |
| `Payments:Webhooks:Enabled` | true | Background worker on/off (off in tests) |
| `Payments:Webhooks:PollSeconds` | 5 | Idle wait between polls |
| `Payments:Refunds:Enabled` / `PollSeconds` | true / 30 | Automatic void/refund worker |
| `Checkout:Expiry:Enabled` / `PollSeconds` | true / 5 | Expiry worker (not in appsettings; code defaults) |
| `Messaging:WhatsApp:Provider` | "" | `openwa` or empty (messages Skipped) |
| `Messaging:WhatsApp:OpenWa:BaseUrl` | – | e.g. `http://localhost:2785` |
| `Messaging:WhatsApp:OpenWa:SessionId` | – | e.g. `erp` |
| `Messaging:WhatsApp:OpenWa:ApiKey` | – | **Secret** |
| `Messaging:WhatsApp:OpenWa:TimeoutSeconds` | 10 | |
| `Messaging:Worker:Enabled` / `PollSeconds` | true / 5 | WhatsApp outbox worker |

Every worker section uses the same `QueueWorkerOptions` (`Enabled` default true, `PollSeconds` default 5, range 1–300). Tests turn all of them off.

---

## 10. Testing

```mermaid
flowchart LR
    subgraph Unit
      U1[Pure logic: HMAC verifier, retry delays,<br/>quote calculator, payload redactor, money]
    end
    subgraph Integration
      F[ApiFactory<br/>one host + one Postgres container] --> T[Test classes<br/>Collection ApiCollection]
      F --> FK1[FakePaymentGateway<br/>intentions, void, refund, Unavailable]
      F --> FK4[FakeWhatsAppSender]
      F --> FK2[Fault injection:<br/>StockFaults, NumberFaults, WebhookFaults,<br/>OrderCancellationFaults, InvoiceIssuingFaults]
      F --> FK3[Real Paymob verifier<br/>with a test secret]
      T --> K[CheckoutTestKit, PaymentTestKit<br/>scenario: product, carrier with Cairo rate, tokens]
    end
```

| Practice | Why |
|---|---|
| **Real PostgreSQL** through Testcontainers | Locks, `SKIP LOCKED`, `xmin`, unique and check constraints behave as in production. |
| **One shared host** (`ApiFactory`), all integration tests in one collection | Fast and avoids two hosts fighting over one database. Tests create their own data with unique names, so they never depend on order. |
| **Fault injection through decorators**, registered once in the factory | Proves rollbacks: stock, document numbers, webhook handling. |
| **Race tests** | Two requests at once: shipping twice, booking twice, invoicing twice, the same webhook processed by two workers. |
| **Facts that depend on global data are unit-tested** | For example "which carrier is cheapest" is tested on the calculator, not through the shared database. |
| **Workers are driven directly** | All four workers are disabled in tests (`UseSetting("<section>:Enabled","false")`); tests call `ProcessNextAsync` in a `DrainAsync` loop until the queue is empty. |
| **Test scopes have no user** | The Showroom filter hides everything in a test's own `DbContext` scope. Assertions read with `IgnoreQueryFilters([QueryFilters.Showroom])`, otherwise "no invoice" passes for the wrong reason. |
| **Shared fakes are reset** | `FakePaymentGateway.Reset()` clears requests, reversals, `Unavailable` and behaviors. Classes that count calls call it in their constructor. |
| **Business vs technical faults** | `InvoiceIssuingFaults` can throw either kind, to prove compensation (refund) vs retry (no refund). |

---

## 11. CI guards
 
`.github/workflows/ci.yml` runs on every PR and push to `main`. A `changes` job (`dorny/paths-filter`) decides whether code changed (`src/**`, `tests/**`, project/lock files, `global.json`, `Directory.*.props`, the workflows); anything else, such as README or docs, skips `build-and-test`, and a skipped job still counts as passing for required checks.
 
| Guard | Blocks |
|---|---|
| Risky logging | `EnableSensitiveDataLogging`, HTTP logging, `{@…}` destructuring |
| Soft-delete entities | `ExecuteDelete` on Products, Employees, Showrooms |
| Stock writes | Any write to stock tables outside `StockService` |
| Business dates | Building a `DateOnly` from a UTC `DateTime` outside `BusinessClock` |
| Payment provider | The word "paymob" anywhere in `src` outside its own folder, the provider switch and appsettings |
| WhatsApp provider | The word "openwa" outside `Infrastructure/Messaging/OpenWa`, the provider switch and appsettings |
| Locked restore | `dotnet restore --locked-mode` |
| Tests ran | Fails if zero tests executed |
