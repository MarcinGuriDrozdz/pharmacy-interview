# Q-commerce dla aptek — Delivery Tracking

Zadanie rekrutacyjne na rolę Tech Lead .NET.

- **Design document całego systemu:** [`docs/DesignDocument-QCommerce-Apteki.docx`](docs/DesignDocument-QCommerce-Apteki.docx)
  (koszyk, checkout, płatność, wybór apteki i rezerwacja, dispatch, śledzenie, NFR, awarie, RODO, plan).
- **Zaimplementowany moduł:** Delivery Tracking — klient widzi, z której apteki przyjedzie dostawa,
  a po odbiorze paczki śledzi kuriera na żywo (trasa, pozycja, postęp, ETA) przez Server-Sent Events.

## Co jest ciekawego w kodzie

| Problem | Gdzie |
|---|---|
| Odczyty GPS powtórzone, spóźnione, nieuporządkowane, z paczek offline | `TrackedDelivery.Apply` — idempotencja po `eventId`, kolejność po `sequence` i czasie urządzenia |
| Zegar telefonu w przyszłości „zamraża” śledzenie; restart aplikacji zeruje licznik | `ClockSkew`, reguła nowej epoki sekwencji w `IsStale` |
| Szum GPS i skoki pozycji | rzut na łamaną (`PlannedRoute`), postęp tylko do przodu, odrzucanie skoków > 22 m/s |
| ETA, które nie eksploduje na światłach | tempo z okna 90 s czasu urządzenia, fallback, progi publikacji |
| Kurier zjeżdża z trasy | `RerouteRequested` → `RerouteWorker` poza ścieżką gorącą → `Reroute` tylko dla aktualnego żądania |
| Współbieżne zapisy do jednej dostawy | `KeyedLock<Guid>` — jeden writer na dostawę (odpowiednik partycji po `deliveryId`) |
| Tysiące klientów na żywo, wolne telefony | `TrackingReadModel`: pełny, wersjonowany stan; bufor per subskrybent z drop-oldest; SSE |
| Prywatność | kurier widoczny tylko między odbiorem a przybyciem, pozycja dopasowana do trasy, cudze zamówienie = 404 |

## Struktura

```
src/DeliveryTracking.Domain        agregat TrackedDelivery, PlannedRoute, TrackingPolicy, zdarzenia (bez zależności)
src/DeliveryTracking.Application   TrackingService, KeyedLock, TrackingReadModel, porty i adaptery in-memory
src/DeliveryTracking.Api           minimal API + SSE, relay zdarzeń, worker zmiany trasy
tests/DeliveryTracking.Tests       testy domeny, współbieżności i integracyjne HTTP/SSE
docs/                              design document, diagramy (SVG/PNG) i generator dokumentu
```

## Uruchomienie

Wymagany .NET 10 SDK.

```bash
dotnet test
dotnet run --project src/DeliveryTracking.Api     # http://localhost:5291
```

Przepływ z konsoli (bash + curl; nagłówki `X-Customer-Id` / `X-Courier-Id` zastępują JWT):

```bash
B=http://localhost:5291
D=aaaaaaaa-0000-0000-0000-000000000001   # delivery
O=bbbbbbbb-0000-0000-0000-000000000001   # order
C=cccccccc-0000-0000-0000-000000000001   # customer
K=dddddddd-0000-0000-0000-000000000001   # courier

# 1. Fulfillment przypisał aptekę (w produkcji: zdarzenie PharmacyAssigned)
curl -X POST $B/internal/deliveries -H "Content-Type: application/json" -d '{
  "deliveryId":"'$D'","orderId":"'$O'","customerId":"'$C'",
  "pharmacy":{"id":"'$K'","name":"Apteka Pod Lwem","address":"ul. Marszalkowska 10, Warszawa","latitude":52.2297,"longitude":21.0122},
  "destination":{"latitude":52.2297,"longitude":21.0298}}'

# 2. Klient otwiera strumień (w drugim terminalu) — od razu widzi aptekę
curl -N -H "X-Customer-Id: $C" $B/orders/$O/tracking/stream

# 3. Kurier odbiera paczkę (w produkcji: zdarzenie ParcelPickedUp)
NOW=$(date -u +%Y-%m-%dT%H:%M:%SZ)
curl -X POST $B/internal/deliveries/$D/pickup -H "Content-Type: application/json" \
  -d '{"courierId":"'$K'","pickedUpAt":"'$NOW'"}'

# 4. Aplikacja kuriera wysyła paczkę odczytów; odpowiedź zawiera decyzję dla każdego
curl -X POST $B/courier/deliveries/$D/locations -H "X-Courier-Id: $K" -H "Content-Type: application/json" -d '{
  "fixes":[{"eventId":"eeeeeeee-0000-0000-0000-000000000001","sequence":1,"recordedAt":"'$NOW'","latitude":52.2297,"longitude":21.0200}]}'

# Bieżący stan bez strumienia
curl -H "X-Customer-Id: $C" $B/orders/$O/tracking
```

Progi (korytarz trasy, promień przybycia, prędkości, tolerancja zegara) są w `appsettings.json`, sekcja `Tracking`.

## Świadome uproszczenia

Stan w pamięci procesu zamiast Redis, blokada w procesie zamiast partycji strumienia, relay w procesie
zamiast outboxa i brokera, trasa po linii prostej zamiast API map, nagłówki zamiast JWT.
Wszystkie te elementy są za portami, a docelowe odpowiedniki opisuje rozdział 12 dokumentu.

## Regeneracja dokumentu

```powershell
docs/diagrams/render.ps1                                   # SVG -> PNG (headless Edge)
cd docs/build; npm install; npm run build                  # docx
docs/build/update-toc.ps1                                  # spis treści (Word)
```
