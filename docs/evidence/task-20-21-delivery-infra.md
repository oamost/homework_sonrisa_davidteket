# Tasks 20–21 evidence — delivery infrastructure

## Delivered

`src/api/Delivery/`: SMTP options/transport/sender, Slack options/redactor/sender, delivery exception, and `AddDeliveryInfrastructure(IServiceCollection, IConfiguration)`.

- `SmtpOptions.Validate()` and `SmtpEmailSender` build a `MailMessage` through an injectable `ISmtpTransport`; per-send timeout via linked cancellation.
- `SlackOptions.Validate()` rejects non-HTTPS/missing webhook; `SlackWebhookSender` posts `{"text": ...}` and treats non-2xx as failure.
- `SlackWebhookRedactor`/`Describe()` keep full webhook URLs and SMTP passwords out of validation errors, exceptions and logs.
- Validation is deferred to send time so an unconfigured channel does not block startup.

## Verification

- 33 delivery tests using fake transport/handler only (200, 400, 429, timeout, redaction); full suite 190 passed; build 0 warnings.
- Documented deviation: implicit TLS on port 465 is not supported by `System.Net.Mail`; explicit STARTTLS is the supported mode.

## Remaining for tasks 20/21

Wire `AddDeliveryInfrastructure` from `Program.cs`, bind `WorldAlerts:Delivery:Smtp/Slack` (secrets from environment/config), map subscriptions to templates, add the test-send action and admin UI. No live email or Slack message was sent.
