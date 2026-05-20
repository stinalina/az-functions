# rememberMe – Azure Functions

Azure Functions Backend für die [**rememberMe**](https://github.com/stinalina/remember-me)-Anwendung. Versendet Erinnerungs- und Willkommens-Mails über [Mailtrap](https://mailtrap.io/) und liest fällige Benachrichtigungen aus einer PostgreSQL-Datenbank.

## Funktionen

### `CrawlDatabaseNightly` (Timer Trigger)
Wird täglich um **23:30 Uhr UTC** ausgeführt.

- Verbindet sich mit der PostgreSQL-Datenbank
- Liest alle Benachrichtigungen (`Notification`), deren `DueDate` dem nächsten Tag entspricht
- Sendet für jede gefundene Benachrichtigung eine Reminder-Mail an den zugehörigen Nutzer über ein Mailtrap-Template

### `SendWelcomeMail` (HTTP Trigger)
**POST** `/api/sendWelcomeMail`

Sendet eine Willkommens-Mail an eine E-Mail-Adresse.

## Technologie-Stack

| Komponente | Technologie |
|---|---|
| Runtime | .NET 10 (isolated worker) |
| Azure Functions | v4 |
| Datenbank | PostgreSQL via Npgsql |
| Mail-Versand | Mailtrap SDK v3 |
| Telemetrie | Application Insights |

