// Generates docs/DesignDocument-QCommerce-Apteki.docx.
// Usage: (cd docs/build && npm install && npm run build). Diagrams: docs/diagrams/render.ps1 (SVG -> PNG).
const fs = require("fs");
const path = require("path");
const {
  AlignmentType, BorderStyle, Document, Footer, Header, HeadingLevel, ImageRun, LevelFormat, Packer,
  PageBreak, PageNumber, Paragraph, ShadingType, Table, TableCell, TableOfContents, TableRow, TextRun,
  WidthType,
} = require("docx");

const OUT = path.join(__dirname, "..", "DesignDocument-QCommerce-Apteki.docx");
const DIAGRAMS = path.join(__dirname, "..", "diagrams");
const NAVY = "1F4E79";
const GREEN = "1B6B50";
const MUTED = "5A6470";
const CONTENT = 9638; // A4 width minus 2 x 2 cm margins, in DXA

// ---------- helpers ----------

// Inline markup: **bold**, `code`.
function runs(text, base = {}) {
  const out = [];
  const re = /(\*\*[^*]+\*\*|`[^`]+`)/g;
  let last = 0;
  let m;
  while ((m = re.exec(text)) !== null) {
    if (m.index > last) out.push(new TextRun({ text: text.slice(last, m.index), ...base }));
    const token = m[0];
    if (token.startsWith("**")) out.push(new TextRun({ text: token.slice(2, -2), bold: true, ...base }));
    else out.push(new TextRun({ text: token.slice(1, -1), font: "Consolas", size: 19, color: "24292F", ...base }));
    last = m.index + token.length;
  }
  if (last < text.length) out.push(new TextRun({ text: text.slice(last), ...base }));
  return out;
}

const p = (text, opts = {}) => new Paragraph({ children: runs(text), spacing: { after: 120 }, ...opts });
const h1 = (text) => new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun(text)], pageBreakBefore: false });
const h2 = (text) => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun(text)] });
const bullet = (text, level = 0) => new Paragraph({ numbering: { reference: "bullets", level }, children: runs(text), spacing: { after: 60 } });
const numbered = (text) => new Paragraph({ numbering: { reference: "steps", level: 0 }, children: runs(text), spacing: { after: 60 } });
const note = (text) => new Paragraph({
  children: runs(text, { color: "3A3A3A" }),
  shading: { type: ShadingType.CLEAR, fill: "F3F7FB", color: "auto" },
  border: { left: { style: BorderStyle.SINGLE, size: 18, color: NAVY, space: 8 } },
  spacing: { before: 60, after: 160 },
  indent: { left: 120 },
});
const decision = (title, text) => new Paragraph({
  children: [new TextRun({ text: `Decyzja: ${title}. `, bold: true, color: GREEN }), ...runs(text)],
  shading: { type: ShadingType.CLEAR, fill: "EEF7F2", color: "auto" },
  border: { left: { style: BorderStyle.SINGLE, size: 18, color: GREEN, space: 8 } },
  spacing: { before: 60, after: 160 },
  indent: { left: 120 },
});

const border = { style: BorderStyle.SINGLE, size: 4, color: "C9CFD6" };
const borders = { top: border, bottom: border, left: border, right: border };

function table(headers, rows, widths) {
  const total = widths.reduce((a, b) => a + b, 0);
  if (total !== CONTENT) throw new Error(`Column widths sum to ${total}, expected ${CONTENT}`);
  const cell = (text, i, header) => new TableCell({
    borders,
    width: { size: widths[i], type: WidthType.DXA },
    shading: header ? { fill: "E4ECF4", type: ShadingType.CLEAR, color: "auto" } : undefined,
    margins: { top: 60, bottom: 60, left: 100, right: 100 },
    children: String(text).split("\n").map((line) => new Paragraph({
      children: runs(line, header ? { bold: true, color: "1A1A1A" } : {}),
      spacing: { after: 20 },
    })),
  });
  return new Table({
    width: { size: CONTENT, type: WidthType.DXA },
    columnWidths: widths,
    rows: [
      ...(headers ? [new TableRow({ tableHeader: true, children: headers.map((h, i) => cell(h, i, true)) })] : []),
      ...rows.map((r) => new TableRow({ children: r.map((c, i) => cell(c, i, false)) })),
    ],
  });
}

const spacer = () => new Paragraph({ children: [], spacing: { after: 80 } });

let figureNo = 0;
function figure(file, caption, widthPx = 640) {
  const [w, h] = { "architecture": [1200, 740], "order-flow": [1200, 800], "order-states": [1200, 300], "tracking-pipeline": [1200, 580] }[file];
  figureNo++;
  return [
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 120, after: 60 },
      children: [new ImageRun({
        type: "png",
        data: fs.readFileSync(path.join(DIAGRAMS, `${file}.png`)),
        transformation: { width: widthPx, height: Math.round((widthPx * h) / w) },
        altText: { title: caption, description: caption, name: file },
      })],
    }),
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { after: 200 },
      children: [new TextRun({ text: `Rysunek ${figureNo}. ${caption}`, italics: true, size: 18, color: MUTED })],
    }),
  ];
}

function code(lines) {
  return lines.map((line, i) => new Paragraph({
    children: [new TextRun({ text: line || " ", font: "Consolas", size: 17, color: "24292F" })],
    shading: { type: ShadingType.CLEAR, fill: "F6F8FA", color: "auto" },
    spacing: { after: 0, before: i === 0 ? 60 : 0, line: 250 },
    indent: { left: 120, right: 120 },
    keepNext: i < lines.length - 1,
    keepLines: true,
    ...(i === lines.length - 1 ? { spacing: { after: 160, line: 250 } } : {}),
  }));
}

// ---------- content ----------

const title = [
  new Paragraph({ spacing: { before: 1800, after: 120 }, children: [new TextRun({ text: "DESIGN DOCUMENT", size: 20, bold: true, color: MUTED, characterSpacing: 40 })] }),
  new Paragraph({ spacing: { after: 120 }, children: [new TextRun({ text: "Q-commerce dla aptek", size: 56, bold: true, color: NAVY })] }),
  new Paragraph({ spacing: { after: 600 }, children: [new TextRun({ text: "Zamówienie, wybór apteki i śledzenie kuriera w czasie rzeczywistym", size: 28, color: "333333" })] }),
  table(null, [
    ["Autor", "Marcin Drozdz (kandydat na Tech Leada)"],
    ["Data", "30 września 2026"],
    ["Status", "Propozycja do przeglądu (RFC)"],
    ["Zakres kodu", "Moduł Delivery Tracking — repozytorium pharmacy-order-backend"],
  ], [2200, 7438]),
  new Paragraph({ spacing: { before: 600 }, children: runs("Dokument opisuje cały system na poziomie architektury i decyzji, a szczegółowo — moduł śledzenia dostawy, który jest zaimplementowany. Tam, gdzie upraszczam, mówię to wprost i podaję, jak wyglądałaby wersja docelowa.", { color: MUTED }) }),
  new Paragraph({ children: [new PageBreak()] }),
];

const toc = [
  new Paragraph({ children: [new TextRun({ text: "Spis treści", bold: true, size: 30, color: NAVY })], spacing: { after: 200 } }),
  new TableOfContents("Spis treści", { hyperlink: true, headingStyleRange: "1-1" }),
  new Paragraph({ children: [new PageBreak()] }),
];

const summary = [
  h1("1. Streszczenie"),
  p("Klient kompletuje koszyk produktów aptecznych, płaci, a system w ciągu kilku minut znajduje jedną aptekę partnerską, która ma cały towar i potwierdzi wydanie. Klient widzi nazwę i adres tej apteki, a po odebraniu paczki przez kuriera — trasę, bieżącą pozycję kuriera, postęp i szacowany czas dojazdu, aktualizowane na żywo."),
  p("Najważniejsze decyzje:"),
  bullet("**Modularny monolit** dla koszyka, zamówień, płatności, fulfillmentu i dispatchu oraz **osobny serwis Delivery Tracking**, bo ma inny profil obciążenia (setki zapisów na sekundę, tysiące otwartych połączeń) i inne wymagania dostępności."),
  bullet("**Preautoryzacja płatności, capture dopiero po akceptacji przez aptekę.** Dostępność towaru potwierdza człowiek w aptece; gdy nikt nie przyjmie zamówienia, zwalniamy blokadę środków zamiast robić zwrot."),
  bullet("**Wybór apteki jako saga z limitem czasu**: miękka rezerwacja w indeksie dostępności, oferta dla najlepszej apteki, 3 minuty na akceptację, maksymalnie trzy próby."),
  bullet("**Śledzenie oparte na regułach po stronie serwera**: idempotencja, kolejność odczytów, dopasowanie do trasy, postęp tylko do przodu, odporne ETA i detekcja objazdu. Klient dostaje zawsze pełny, wersjonowany stan przez **Server-Sent Events**."),
  bullet("V1 obejmuje **wyłącznie produkty bez recepty** — sprzedaż wysyłkowa leków na receptę jest w Polsce zabroniona (do potwierdzenia z działem prawnym, szczegóły w założeniach)."),
];

const scope = [
  h1("2. Kontekst, zakres i założenia"),
  h2("2.1 Problem"),
  p("Q-commerce oznacza tu dostawę z lokalnej apteki w 30–60 minut, a nie wysyłkę z magazynu centralnego. Stan magazynowy jest rozproszony po aptekach, zmienia się niezależnie od nas (klienci stacjonarni) i jest znany z opóźnieniem. To odróżnia ten system od zwykłego sklepu internetowego i jest głównym źródłem złożoności: nie da się „zarezerwować” towaru wyłącznie w naszej bazie."),
  h2("2.2 Zakres"),
  table(["W zakresie v1", "Poza zakresem v1"], [
    ["Katalog OTC, suplementy, dermokosmetyki; indeks dostępności per apteka", "Leki na receptę (Rx), e-recepta, konsultacje farmaceutyczne online"],
    ["Koszyk, checkout, płatność (BLIK, karta) przez zewnętrznego operatora", "Programy lojalnościowe, kupony, subskrypcje"],
    ["Wybór apteki, rezerwacja, panel akceptacji dla apteki", "Dzielenie zamówienia na kilka aptek"],
    ["Przydział kuriera z floty platformy (najbliższy wolny)", "Optymalizacja tras wielu zamówień (batching), własna flota aptek"],
    ["Śledzenie kuriera na żywo, ETA, powiadomienia", "Własne mapy i routing (używamy dostawcy)"],
    ["Jedno miasto na start (Warszawa)", "Wiele stref czasowych, wiele walut"],
  ], [4819, 4819]),
  spacer(),
  h2("2.3 Założenia"),
  p("Założenia upraszczają projekt, ale każde ma wskazaną konsekwencję — tak, żeby było wiadomo, co się zmieni, gdy założenie przestanie obowiązywać."),
  table(["#", "Założenie", "Konsekwencja / uzasadnienie"], [
    ["Z1", "Tylko produkty bez recepty.", "Prawo farmaceutyczne dopuszcza sprzedaż wysyłkową wyłącznie leków OTC (do potwierdzenia z prawnikami). Katalog ma flagę `RequiresPrescription`; takie produkty nie trafiają do koszyka. Rx to osobny projekt (odbiór osobisty)."],
    ["Z2", "Jedno zamówienie realizuje jedna apteka.", "Prostszy fulfillment, jedna paczka, jeden kurier. Jeśli żadna apteka nie ma całości, zamówienie nie zostanie przyjęte, a klient dostanie propozycję usunięcia brakujących pozycji."],
    ["Z3", "Apteki partnerskie wystawiają stany ze swojego systemu aptecznego (feed co 1–5 min) i mają tablet z panelem.", "Indeks dostępności jest przybliżeniem; źródłem prawdy jest akceptacja zamówienia przez farmaceutę."],
    ["Z4", "Jednolity cennik platformy.", "Cena nie zależy od wybranej apteki, więc wybór apteki po płatności nie zmienia kwoty. Rozliczenia z aptekami poza zakresem."],
    ["Z5", "Kurierzy są flotą platformy, z naszą aplikacją.", "Mamy kontrolę nad kontraktem GPS (częstotliwość, identyfikatory zdarzeń, bufor offline)."],
    ["Z6", "Płatności, geokodowanie, routing i push obsługują dostawcy zewnętrzni.", "Integracje za adapterami; każda ma plan degradacji (sekcja 9)."],
    ["Z7", "Klient ma konto (logowanie).", "Śledzenie i dane zamówienia są autoryzowane tokenem klienta; brak zakupów gościnnych w v1."],
    ["Z8", "Produkty wymagające 2–8°C tylko z apteki z torbą termiczną; limity ilościowe per produkt.", "Filtr zdolności apteki przy wyborze; walidacja limitów w koszyku."],
  ], [600, 3600, 5438]),
  spacer(),
  h2("2.4 Skala i wymagania niefunkcjonalne"),
  table(["Obszar", "Cel v1", "Z czego wynika"], [
    ["Zamówienia", "8 000 / dzień, szczyt 40 / min", "jedno miasto, ~300 aptek partnerskich"],
    ["Aktywne dostawy", "do 1 000 równocześnie", "40 / min × ~25 min w drodze"],
    ["Pozycje GPS", "~250 odczytów / s, ~125 żądań / s", "odczyt co 4 s w ruchu, batch co ~8 s"],
    ["Strumienie klientów", "do 5 000 otwartych połączeń", "1–3 urządzenia na dostawę + zapas"],
    ["Opóźnienie pozycji", "p95 < 2 s od odczytu do ekranu klienta", "odczucie „na żywo”"],
    ["API koszyka i checkoutu", "p95 < 300 ms", "konwersja"],
    ["Dostępność", "zamówienia 99,9%, śledzenie 99,5%", "śledzenie może zdegradować się do ostatniej pozycji"],
    ["Spójność", "płatność pobrana dokładnie raz, brak podwójnej rezerwacji", "idempotencja, saga, outbox"],
    ["Dane", "zawartość zamówienia traktowana jak dane o zdrowiu (art. 9 RODO)", "szyfrowanie, minimalizacja, retencja"],
  ], [2300, 3700, 3638]),
];

const architecture = [
  h1("3. Architektura"),
  ...figure("architecture", "Widok komponentów. Zielonym kolorem oznaczono moduł zaimplementowany w repozytorium."),
  decision("modularny monolit + osobny serwis śledzenia", "Na start jeden zespół (5–7 osób) i jedna domena zamówienia — mikroserwisy dodałyby koszt operacyjny bez zysku. Moduły monolitu mają osobne schematy bazy, komunikują się zdarzeniami przez outbox i nie sięgają do cudzych tabel, więc każdy da się wydzielić później. Tracking wydzielamy od razu: skaluje się inaczej (zapis ciągły, długie połączenia), a jego awaria nie może zatrzymać przyjmowania zamówień."),
  h2("3.1 Moduły i odpowiedzialności"),
  table(["Moduł", "Odpowiada za", "Dane, których jest właścicielem"], [
    ["Katalog i dostępność", "produkty, flagi (OTC, chłodnicze, limity), indeks stanów per apteka z feedu", "Product, PharmacyStock (ilość, znacznik czasu feedu, rezerwacje miękkie)"],
    ["Koszyk", "pozycje, walidacja limitów i dostępności w okolicy, wycena", "Cart (Redis, TTL 7 dni)"],
    ["Checkout i zamówienia", "złożenie zamówienia, maszyna stanów, orkiestracja sagi", "Order, OrderLine, OrderStatusHistory, Outbox"],
    ["Płatności", "adapter operatora: preautoryzacja, capture, void, zwrot, webhooki", "Payment, PaymentAttempt (idempotency key)"],
    ["Fulfillment", "wybór apteki, rezerwacja, oferta i akceptacja, ponowny wybór", "Assignment, Offer (z terminem), Reservation"],
    ["Apteki", "dane apteki, godziny, pojemność kompletacji, panel", "Pharmacy (PostGIS: lokalizacja, strefa)"],
    ["Dispatch", "przydział kuriera, odbiór, potwierdzenie doręczenia", "Courier, CourierShift, DeliveryJob"],
    ["Delivery Tracking", "przyjmowanie pozycji, postęp, ETA, objazdy, widok klienta na żywo", "TrackedDelivery (Redis), read model, historia pozycji (30 dni)"],
    ["Powiadomienia", "push, SMS, e-mail na podstawie zdarzeń", "szablony, preferencje, log wysyłek"],
  ], [2100, 4300, 3238]),
  spacer(),
  h2("3.2 Technologie"),
  table(["Obszar", "Wybór", "Dlaczego"], [
    ["Backend", ".NET 10 (LTS), ASP.NET Core minimal API", "kompetencje zespołu, wydajność, natywne SSE i TimeProvider"],
    ["Baza transakcyjna", "PostgreSQL + PostGIS", "transakcje dla rezerwacji, zapytania przestrzenne o apteki"],
    ["Stan gorący", "Redis", "stan aktywnych dostaw, read model śledzenia, pub/sub dla SSE, koszyki"],
    ["Zdarzenia biznesowe", "Azure Service Bus (lub RabbitMQ)", "kolejki z DLQ, sesje, opóźnione wiadomości dla limitów czasu sagi"],
    ["Strumień GPS", "Event Hubs / Kafka, klucz partycji = deliveryId", "kolejność per dostawa i jeden konsument partycji = jeden writer"],
    ["Push do klienta", "Server-Sent Events + Redis pub/sub", "patrz decyzja w 7.6"],
    ["Obserwowalność", "OpenTelemetry → Grafana / Application Insights", "śledzenie przez granice modułów i brokera"],
    ["Uruchomienie", "kontenery (AKS / Azure Container Apps), IaC", "niezależne skalowanie trackingu"],
  ], [2100, 3500, 4038]),
];

const orderFlow = [
  h1("4. Przepływ zamówienia"),
  ...figure("order-flow", "Przepływ od złożenia zamówienia do doręczenia."),
  ...figure("order-states", "Maszyna stanów zamówienia (właściciel: moduł Checkout i zamówienia)."),
  h2("4.1 Koszyk"),
  bullet("Koszyk przechowuje identyfikatory produktów i ilości; cena jest liczona przy każdym odczycie z cennika platformy (Z4), a zamrażana dopiero przy złożeniu zamówienia."),
  bullet("Przy dodaniu produktu sprawdzamy flagi: produkty Rx są odrzucane, limity ilościowe (np. dla substancji z ograniczeniami) egzekwowane per zamówienie."),
  bullet("Adres dostawy jest geokodowany przy wyborze; poza strefą dostaw checkout jest zablokowany. Na tej podstawie koszyk pokazuje informacyjnie „dostępne w okolicy” — z indeksu stanów w promieniu ~4 km, bez gwarancji."),
  h2("4.2 Checkout i płatność"),
  bullet("`POST /orders` przyjmuje nagłówek `Idempotency-Key`; ponowienie z tym samym kluczem zwraca to samo zamówienie. Zamówienie powstaje w stanie `PendingPayment` z zamrożonymi cenami i adresem."),
  bullet("Moduł płatności wykonuje **preautoryzację** (blokadę środków). Wynik przychodzi webhookiem; webhooki są idempotentne po identyfikatorze transakcji operatora, a brak webhooka w 2 minuty uruchamia odpytanie statusu."),
  bullet("**Capture** następuje po akceptacji przez aptekę. Gdy zamówienia nie da się zrealizować, wykonujemy **void** — klient nie czeka na zwrot, a my nie płacimy prowizji za zwrot."),
  h2("4.3 Saga realizacji"),
  p("Orkiestratorem jest moduł zamówień (process manager). Każdy krok to komenda i zdarzenie przez broker; limity czasu to wiadomości opóźnione. Stan sagi jest zapisywany razem ze stanem zamówienia w jednej transakcji z outboxem, więc restart w dowolnym momencie jest bezpieczny."),
  table(["Krok", "Sukces", "Porażka / limit czasu", "Kompensacja"], [
    ["Preautoryzacja", "OrderAuthorized", "odrzucenie, brak odpowiedzi 15 min", "Cancelled"],
    ["Wybór apteki + oferta", "PharmacyAccepted", "odrzucenie lub 3 min bez odpowiedzi", "zwolnij rezerwację, następna apteka (maks. 3), potem void"],
    ["Capture", "PaymentCaptured", "błąd operatora", "ponawianie z backoffem; eskalacja do obsługi"],
    ["Przydział kuriera", "CourierAssigned", "brak kuriera 10 min", "poszerzenie promienia, alert do dispatchera"],
    ["Odbiór", "ParcelPickedUp", "apteka nie wyda towaru", "ponowny wybór apteki albo anulowanie ze zwrotem"],
    ["Doręczenie", "Delivered (kod od klienta)", "klient nieobecny", "zwrot paczki do apteki, procedura reklamacji"],
  ], [1900, 2100, 2700, 2938]),
];

const pharmacy = [
  h1("5. Wybór apteki i rezerwacja"),
  p("To najbardziej ryzykowny biznesowo element systemu: zła decyzja oznacza anulowane zamówienie albo długie oczekiwanie. Rozdzielamy dwie rzeczy: **szybki wybór kandydata** na podstawie przybliżonych danych i **potwierdzenie przez człowieka**, które jest źródłem prawdy."),
  h2("5.1 Filtrowanie i ranking"),
  numbered("**Filtr twardy** (zapytanie PostGIS + indeks stanów): apteka otwarta jeszcze co najmniej 45 minut, w promieniu dojazdu (~4 km / 15 min), ma zdolności (np. chłodnicze), ma wolną pojemność kompletacji, a dla każdej pozycji `dostępne − zarezerwowane ≥ ilość`."),
  numbered("**Ranking**: `score = w1·czas dojazdu do klienta + w2·obciążenie apteki + w3·wiek danych o stanie + w4·historyczny odsetek odrzuceń`. Wagi w konfiguracji; start: czas dojazdu dominuje."),
  numbered("**Oferta** dla najlepszej apteki, z terminem 3 minut. Równolegle wysyłamy ofertę tylko do jednej apteki — żeby nie kompletować tego samego zamówienia dwa razy."),
  h2("5.2 Miękka rezerwacja"),
  p("Indeks dostępności ma kolumnę `reserved`. Rezerwacja wszystkich pozycji zamówienia jest jedną transakcją warunkową — albo cała, albo wcale:"),
  ...code([
    "UPDATE pharmacy_stock",
    "   SET reserved = reserved + l.qty",
    "  FROM (VALUES (@p1, @q1), (@p2, @q2)) AS l(product_id, qty)",
    " WHERE pharmacy_id = @pharmacy AND pharmacy_stock.product_id = l.product_id",
    "   AND available - reserved >= l.qty;",
    "-- liczba zmienionych wierszy != liczba pozycji  =>  ROLLBACK i następny kandydat",
  ]),
  p("Rezerwacja wygasa razem z ofertą (odrzucenie, limit czasu) albo zamienia się w zdjęcie ze stanu przy odbiorze paczki. Dzięki temu dwa równoległe zamówienia nie trafią do tej samej apteki po „ostatnie opakowanie”, mimo że feed stanów spóźnia się o kilka minut. Każdy nowy feed nadpisuje `available`, nie ruszając `reserved`."),
  h2("5.3 Kiedy klient widzi aptekę"),
  decision("apteka jest pokazywana po akceptacji, nie po wyborze", "Wybrany kandydat będzie się czasem zmieniał (odrzucenie, brak odpowiedzi — skalę zmierzymy w pilotażu). Pokazanie apteki, która potem się zmienia, jest gorsze niż 1–3 minuty statusu „Szukamy apteki”. Po akceptacji Fulfillment publikuje `PharmacyAssigned`, a Tracking od razu wystawia widok z nazwą i adresem apteki — jeszcze zanim pojawi się kurier. Rzadka zmiana apteki po akceptacji (apteka jednak nie wyda) jest komunikowana powiadomieniem."),
  h2("5.4 Przydział kuriera"),
  p("Dispatch dostaje zlecenie z przewidywanym czasem gotowości paczki (średni czas kompletacji apteki) i wybiera najbliższego wolnego kuriera tak, żeby dojechał na ten czas. Kurier ma 60 s na przyjęcie zlecenia; odmowa lub brak odpowiedzi — następny kurier. Odbiór to skan kodu paczki w aptece: zdarzenie `ParcelPickedUp` rozpoczyna śledzenie na żywo."),
];

const tracking = [
  h1("6. Moduł Delivery Tracking — przegląd"),
  p("Moduł odpowiada za wymaganie „klient widzi, z jakiej apteki przyjedzie dostawa, i w czasie rzeczywistym drogę kuriera”. Wybrałem go do implementacji, bo łączy kilka nietrywialnych problemów: niezaufane i nieuporządkowane dane z telefonów, spójność przy współbieżnych zapisach, wyliczanie postępu i ETA oraz dystrybucję stanu do tysięcy klientów."),
  ...figure("tracking-pipeline", "Przepływ pozycji kuriera i odpowiedniki elementów w kodzie."),
  h2("6.1 Cykl życia dostawy w module"),
  table(["Faza", "Wejście", "Co widzi klient"], [
    ["AwaitingPickup", "`PharmacyAssigned` z Fulfillment (apteka, adres klienta)", "nazwę i adres apteki, status „apteka kompletuje zamówienie”; bez kuriera"],
    ["InTransit", "`ParcelPickedUp` z aplikacji kuriera; trasa od dostawcy map", "trasę, pozycję kuriera dopasowaną do trasy, postęp, pozostały dystans, ETA"],
    ["Arrived", "pozostały dystans ≤ 35 m", "„kurier na miejscu”; strumień się kończy"],
  ], [1700, 4000, 3938]),
  spacer(),
  note("Pozycji kuriera przed odbiorem nie pokazujemy. Kurier może wtedy realizować inne zlecenie, a jego położenie nie jest informacją dla klienta. Po dotarciu na miejsce pozycja przestaje być publikowana."),
];

const trackingDeep = [
  h1("7. Śledzenie — szczegóły projektu"),
  h2("7.1 Kontrakt aplikacji kuriera"),
  p("Aplikacja wysyła paczki odczytów `POST /courier/deliveries/{deliveryId}/locations` (tożsamość kuriera z tokenu). Odczyt zawiera:"),
  table(["Pole", "Znaczenie"], [
    ["`eventId`", "unikalny identyfikator odczytu generowany na urządzeniu; ponowienie wysyłki używa tego samego — to klucz idempotencji"],
    ["`sequence`", "licznik rosnący per dostawa, trwały na urządzeniu; główne kryterium kolejności"],
    ["`recordedAt`", "czas pomiaru na urządzeniu (nie czas przyjścia); z niego liczymy tempo"],
    ["`latitude`, `longitude`", "WGS84"],
  ], [2400, 7238]),
  spacer(),
  p("Częstotliwość: co 4 s w ruchu, co 15 s na postoju (oszczędność baterii). Bez sieci aplikacja buforuje odczyty i wysyła je później jedną paczką (do 200 odczytów) — serwer musi więc przyjmować dane spóźnione, nieuporządkowane i powtórzone."),
  h2("7.2 Reguły przyjęcia odczytu"),
  p("Każdy odczyt dostaje jedną z decyzji. Kolejność sprawdzeń ma znaczenie i jest w kodzie jawna (`TrackedDelivery.Apply`)."),
  table(["Decyzja", "Kiedy", "Skutek"], [
    ["NotPickedUp", "dostawa jeszcze nie odebrana", "ignorowany, nie zużywa eventId"],
    ["ClockSkew", "`recordedAt` ponad 30 s w przyszłości względem serwera", "odrzucony bez przesuwania znacznika czasu; urządzenie może wysłać ponownie z poprawnym czasem"],
    ["Duplicate", "ten sam `eventId` już przetworzony", "no-op — ponowienie nie przesuwa kuriera drugi raz"],
    ["Stale", "starszy czas niż ostatni przyjęty, sprzed odbioru, albo niższy `sequence` bez oznak restartu aplikacji", "no-op — spóźniony pakiet nie cofa kuriera"],
    ["OffRoute", "punkt dalej niż 120 m od trasy", "pozycja stoi; po 20 s poza trasą — jedno żądanie nowej trasy"],
    ["Outlier", "skok do przodu szybszy niż 22 m/s względem ostatniej próbki", "odrzucony; znacznik sekwencji nie rośnie, więc spóźniony odczyt pośredni nadal może być przyjęty"],
    ["BackwardIgnored", "rzut na trasę więcej niż 8 m za bieżącym postępem", "pozycja stoi (szum GPS, powrót po zaparkowaniu)"],
    ["Applied", "pozostałe", "postęp, ETA, ewentualnie przybycie"],
    ["AlreadyArrived", "dostawa zakończona", "no-op"],
  ], [1800, 4200, 3638]),
  spacer(),
  p("Dwa przypadki brzegowe, które łatwo przeoczyć:"),
  bullet("**Zegar urządzenia w przyszłości.** Gdyby taki odczyt został przyjęty, przesunąłby znacznik czasu i każdy kolejny, poprawny odczyt wyglądałby na przestarzały — śledzenie zamarłoby do końca dostawy. Dlatego tolerancja 30 s i odrzucenie bez zużycia `eventId`."),
  bullet("**Restart aplikacji zeruje licznik sekwencji.** Niższa sekwencja jest akceptowana jako nowa „epoka” tylko wtedy, gdy czas odczytu jest co najmniej 60 s nowszy niż ostatni przyjęty. Spóźniony pakiet ma zawsze starszy czas, więc nie przejdzie tej reguły."),
  h2("7.3 Dopasowanie do trasy i postęp"),
  p("Trasa od dostawcy map jest łamaną. Odczyt rzutujemy na najbliższy odcinek (lokalny układ metryczny wystarcza w skali miasta) i dostajemy dystans wzdłuż trasy oraz odległość od niej. Przy remisie dwóch odcinków wygrywa dalszy, żeby pętle trasy nie cofały postępu. Klientowi pokazujemy **punkt dopasowany do trasy**, nie surowy GPS — znacznik nie skacze po budynkach, a my nie ujawniamy dokładnej pozycji telefonu kuriera."),
  p("Postęp rośnie monotonicznie. Przybycie następuje, gdy do końca trasy zostaje ≤ 35 m; jest ostateczne i emitowane raz."),
  h2("7.4 ETA"),
  bullet("Tempo liczymy z próbek z ostatnich 90 s **czasu urządzenia** (nie czasu przyjścia — paczka offline nie może zaniżyć prędkości)."),
  bullet("Gdy okno jest krótsze niż 5 s, kurier stoi (< 1,2 m/s) albo brak próbek — używamy prędkości nominalnej 6 m/s. Bez tego postój na światłach dawałby ETA „za 3 godziny”."),
  bullet("Prędkość jest ograniczona z góry (22 m/s). Zmianę ETA publikujemy dopiero, gdy przesunie się o ≥ 20 s — ekran klienta nie „mruga”. Postęp publikujemy co ≥ 8 m. Te progi zmniejszają ruch do klientów kilkukrotnie."),
  bullet("V1 celowo używa prostego modelu. Wersja docelowa: ETA z API dostawcy map (korki) liczone rzadko, korygowane tempem kuriera między odświeżeniami."),
  h2("7.5 Objazd i zmiana trasy"),
  p("Kurier, który ponad 20 s jedzie poza trasą, wywołuje jedno zdarzenie `RerouteRequested` (identyfikator = `eventId` odczytu, więc deterministyczny przy ponownym przetworzeniu). Adapter routingu poza ścieżką gorącą pobiera nową trasę od bieżącej pozycji i odsyła ją z tym samym identyfikatorem. Agregat przyjmuje odpowiedź tylko, jeśli żądanie jest wciąż aktualne — jeśli kurier w międzyczasie wrócił na trasę, odpowiedź jest ignorowana. Po zmianie trasy klient dostaje nową łamaną (`RouteChanged`), a postęp uwzględnia już przejechany odcinek."),
  h2("7.6 Dystrybucja stanu do klienta"),
  decision("Server-Sent Events z pełnym, wersjonowanym stanem", "Komunikacja jest jednokierunkowa, więc WebSocket/SignalR nie daje przewagi, a wymaga sticky sessions albo usługi zarządzanej. SSE to zwykły HTTP: przechodzi przez gateway i CDN, ma ponowne łączenie w standardzie i jest natywne w ASP.NET Core 10. Alternatywa rozważona: SignalR z Azure SignalR Service — sensowna, gdyby doszedł kanał dwukierunkowy (np. czat z kurierem)."),
  bullet("Każda wiadomość to **pełny stan** (faza, apteka, pozycja, postęp, ETA) z numerem wersji, nie delta. Zgubiona wiadomość albo ponowne połączenie niczego nie psują — klient potrzebuje tylko najnowszej."),
  bullet("Pierwsza wiadomość po połączeniu to bieżący stan z trasą; kolejne zawierają trasę tylko po jej zmianie. Klient odrzuca wiadomości ze starszą wersją."),
  bullet("Serwer najpierw subskrybuje, potem czyta stan — nic nie ginie w oknie między tymi krokami. Bufor per klient jest ograniczony i odrzuca najstarsze wiadomości: wolny telefon nigdy nie blokuje zapisu."),
  bullet("Strumień kończy się po przybyciu. Awaryjnie aplikacja odpytuje `GET /orders/{id}/tracking` co 10 s."),
  h2("7.7 Współbieżność i skalowanie"),
  bullet("**Jeden writer na dostawę.** Strumień GPS jest partycjonowany po `deliveryId`, a partycję czyta jeden konsument — zapisy do jednej dostawy są sekwencyjne bez rozproszonych blokad. W kodzie odpowiada temu `KeyedLock<Guid>` w procesie."),
  bullet("Procesory są bezstanowe; stan aktywnych dostaw jest w Redis (odczyt, zmiana, zapis z kontrolą wersji jako druga linia obrony przy rebalansowaniu partycji)."),
  bullet("Wolne operacje (routing) nie są wykonywane pod blokadą — przez to czas obsługi paczki odczytów to pojedyncze milisekundy."),
  bullet("Węzły SSE są bezstanowe: subskrybują kanał Redis dla zamówień swoich klientów. 5 000 połączeń to kilka procent możliwości jednego węzła Kestrel; skalujemy poziomo dla dostępności."),
  bullet("Zdarzenia wychodzą przez outbox zapisany razem ze stanem — `CourierArrived` trafia do modułu zamówień dokładnie raz z punktu widzenia biznesu (konsumenci są idempotentni)."),
  h2("7.8 Prywatność i bezpieczeństwo śledzenia"),
  bullet("Widok zamówienia jest dostępny tylko dla właściciela (`sub` z tokenu). Cudze zamówienie zwraca 404, nie 403 — nie ujawniamy, że istnieje."),
  bullet("Odczyty przyjmujemy tylko od kuriera przypisanego do dostawy (403 dla innych)."),
  bullet("Klient widzi pozycję dopasowaną do trasy, tylko między odbiorem a przybyciem. Widok nie zawiera zawartości zamówienia (dane o zdrowiu)."),
  bullet("Surowa historia pozycji jest przechowywana 30 dni (reklamacje), potem usuwana."),
];

const dataApi = [
  h1("8. Dane i API"),
  h2("8.1 Główne encje"),
  table(["Encja", "Kluczowe pola", "Magazyn"], [
    ["Order", "id, customerId, status, adres (geo), sumy, pharmacyId, courierId, version", "PostgreSQL (schemat orders)"],
    ["OrderLine", "orderId, productId, qty, cena zamrożona", "PostgreSQL"],
    ["PharmacyStock", "pharmacyId, productId, available, reserved, feedAt", "PostgreSQL (schemat catalog)"],
    ["Offer", "orderId, pharmacyId, expiresAt, status", "PostgreSQL (schemat fulfillment)"],
    ["TrackedDelivery", "deliveryId, orderId, faza, trasa, postęp, znaczniki kolejności, przetworzone eventId", "Redis (aktywne), archiwum w PostgreSQL"],
    ["TrackingView", "orderId, version, apteka, pozycja, postęp, ETA, trasa", "Redis (read model)"],
    ["Outbox", "id, type, payload, occurredAt, publishedAt", "w każdym schemacie modułu"],
  ], [2000, 5000, 2638]),
  spacer(),
  h2("8.2 API"),
  table(["Metoda i ścieżka", "Kto", "Opis"], [
    ["`GET /catalog/products?query=`", "klient", "wyszukiwanie OTC"],
    ["`PUT /carts/me/items/{productId}`", "klient", "ustawienie ilości; walidacja limitów"],
    ["`POST /orders`", "klient", "złożenie zamówienia; `Idempotency-Key`"],
    ["`POST /payments/webhooks/{provider}`", "operator", "status płatności; weryfikacja podpisu"],
    ["`POST /pharmacy/offers/{id}/accept`", "apteka", "akceptacja oferty w panelu"],
    ["`POST /internal/deliveries`", "Fulfillment (zdarzenie)", "`PharmacyAssigned` → widok z apteką *"],
    ["`POST /internal/deliveries/{id}/pickup`", "Dispatch (zdarzenie)", "`ParcelPickedUp` → start śledzenia *"],
    ["`POST /courier/deliveries/{id}/locations`", "kurier", "paczka odczytów GPS; decyzja per odczyt *"],
    ["`GET /orders/{id}/tracking`", "klient", "bieżący stan śledzenia *"],
    ["`GET /orders/{id}/tracking/stream`", "klient", "strumień SSE *"],
  ], [3900, 2000, 3738]),
  new Paragraph({ children: runs("* zaimplementowane. Endpointy `/internal` zastępują w repozytorium konsumentów brokera.", { size: 18, color: MUTED }), spacing: { before: 60, after: 160 } }),
];

const reliability = [
  h1("9. Niezawodność i sytuacje awaryjne"),
  table(["Sytuacja", "Zachowanie systemu"], [
    ["Operator płatności nie odpowiada", "timeout + odpytanie statusu; zamówienie w `PendingPayment` do 15 min, potem anulowanie. Checkout pokazuje „płatność w toku”."],
    ["Żadna apteka nie przyjmuje", "3 próby, potem void preautoryzacji i powiadomienie z propozycją zmiany koszyka."],
    ["Feed stanów apteki nie przychodzi", "wiek danych obniża ranking; po 30 min apteka wypada z wyboru."],
    ["Brak kuriera", "poszerzenie promienia, alert do dyspozytora; klient widzi aktualny status i szacunek."],
    ["Kurier traci zasięg", "klient widzi ostatnią pozycję z informacją o czasie; po powrocie zasięgu paczka offline uzupełnia postęp bez cofania."],
    ["Dostawca map niedostępny", "przy odbiorze: trasa prosta apteka–klient z szerszym korytarzem (degradacja jakości, nie funkcji); objazdy nie są przeliczane."],
    ["Awaria procesora pozycji", "partycję przejmuje inny konsument, stan jest w Redis; zgubione odczyty są nieszkodliwe — kolejny odczyt koryguje pozycję."],
    ["Awaria Redis (tracking)", "odtworzenie stanu z ostatnich odczytów w strumieniu (retencja 24 h); zamówienia działają dalej."],
    ["Zdublowane wiadomości brokera", "wszyscy konsumenci idempotentni (klucz zdarzenia, maszyna stanów)."],
  ], [3000, 6638]),
];

const security = [
  h1("10. Bezpieczeństwo, RODO i obserwowalność"),
  h2("10.1 Bezpieczeństwo i dane osobowe"),
  bullet("Uwierzytelnianie OIDC; osobne typy tokenów dla klienta, kuriera i apteki; ruch `/internal` tylko w sieci usług (mTLS)."),
  bullet("Zawartość zamówienia może ujawniać stan zdrowia — traktujemy ją jak dane szczególnej kategorii: szyfrowanie w spoczynku, dostęp w panelu apteki tylko do jej zamówień, brak nazw produktów w powiadomieniach push i w widoku śledzenia."),
  bullet("Kurier widzi adres i numer mieszkania tylko w trakcie dostawy; po doręczeniu dane znikają z aplikacji."),
  bullet("Retencja: pozycje GPS 30 dni, zamówienia zgodnie z obowiązkiem księgowym; logi bez danych osobowych (identyfikatory zamiast adresów)."),
  h2("10.2 Obserwowalność"),
  table(["Wskaźnik", "Alert, gdy"], [
    ["Czas do akceptacji przez aptekę (p50, p95)", "p95 > 5 min przez 15 min"],
    ["Odsetek zamówień anulowanych z braku apteki", "> 5% w godzinie"],
    ["Opóźnienie odczyt → ekran klienta", "p95 > 2 s"],
    ["Rozkład decyzji dla odczytów GPS (Stale, Outlier, ClockSkew)", "nagły wzrost — zwykle błąd wydania aplikacji kuriera"],
    ["Aktywne dostawy bez odczytu > 60 s", "> 5% aktywnych"],
    ["Rozjazd ETA a faktyczny czas dojazdu", "mediana > 4 min w dniu"],
  ], [5200, 4438]),
];

const plan = [
  h1("11. Plan realizacji"),
  table(["Etap", "Zakres", "Kryterium wyjścia"], [
    ["M0 (2 tyg.)", "szkielet, CI/CD, IaC, observability, kontrakty zdarzeń", "pusty przepływ zamówienia działa na środowisku testowym"],
    ["M1 (6 tyg.)", "katalog, koszyk, checkout, płatności, panel apteki, ręczny dispatch", "zamówienia testowe z 5 aptekami, kurier przydzielany ręcznie"],
    ["M2 (4 tyg.)", "automatyczny wybór apteki i rezerwacja, Dispatch, Tracking", "pilotaż: 20 aptek, jedna dzielnica"],
    ["M3 (4 tyg.)", "ETA z danych o ruchu, strojenie rankingu, obsługa reklamacji", "całe miasto, SLO spełnione przez 2 tygodnie"],
  ], [1600, 4700, 3338]),
  spacer(),
  p("Zespół: 4 backend (.NET), 2 mobile, 1 frontend web, QA, Tech Lead. Moduł Tracking jest niezależny od reszty poza kontraktami zdarzeń i może być rozwijany równolegle od M1."),
];

const implemented = [
  h1("12. Zaimplementowany moduł"),
  p("Repozytorium zawiera moduł Delivery Tracking w .NET 10: od przyjęcia informacji o aptece, przez odbiór i odczyty GPS, po strumień SSE dla klienta. Nie ma w nim dużej ilości kodu — są za to elementy, które w takim systemie zwykle psują się na produkcji."),
  table(["Projekt", "Zawartość"], [
    ["DeliveryTracking.Domain", "`TrackedDelivery` (reguły z 7.2–7.5), `PlannedRoute` (rzut na łamaną), `TrackingPolicy` (progi), zdarzenia domenowe. Bez zależności."],
    ["DeliveryTracking.Application", "`TrackingService` (przypadki użycia, jeden writer per dostawa), `KeyedLock`, `TrackingReadModel` (projekcja + fan-out, drop-oldest), porty i adaptery in-memory."],
    ["DeliveryTracking.Api", "minimal API, SSE (`TypedResults.ServerSentEvents`), `TrackingEventDispatcher` (relay outboxa), `RerouteWorker`, mapowanie błędów na ProblemDetails."],
    ["DeliveryTracking.Tests", "33 testy: reguły domeny, współbieżność, zmiana trasy, wolny subskrybent, integracyjne przez HTTP i SSE (WebApplicationFactory)."],
  ], [3000, 6638]),
  spacer(),
  h2("12.1 Co jest świadomie uproszczone"),
  table(["W kodzie", "Docelowo"], [
    ["stan w pamięci procesu (`InMemoryDeliveryRepository`)", "Redis z kontrolą wersji; archiwum w PostgreSQL"],
    ["`KeyedLock` w procesie", "partycja strumienia po `deliveryId`"],
    ["dispatcher w procesie zamiast outboxa", "outbox w tej samej transakcji co stan + relay do brokera"],
    ["`StraightLineRouteProvider` (łamana po linii prostej)", "API dostawcy map; kod agregatu się nie zmienia"],
    ["nagłówki `X-Customer-Id`, `X-Courier-Id`", "JWT (`sub`), polityki autoryzacji"],
    ["endpointy `/internal`", "konsumenci zdarzeń `PharmacyAssigned`, `ParcelPickedUp`"],
  ], [4600, 5038]),
  spacer(),
  h2("12.2 Uruchomienie"),
  ...code([
    "dotnet test",
    "dotnet run --project src/DeliveryTracking.Api      # http://localhost:5291",
  ]),
  p("Przykładowe wywołania (rejestracja, odbiór, odczyty, strumień) są w `README.md`."),
];

const risks = [
  h1("13. Ryzyka i otwarte pytania"),
  table(["Ryzyko / pytanie", "Wpływ", "Plan"], [
    ["Interpretacja przepisów dot. sprzedaży wysyłkowej i transportu produktów leczniczych", "wysoki", "opinia prawna przed M1; model danych ma już flagi produktu i zdolności apteki"],
    ["Jakość i opóźnienie feedów stanów z systemów aptecznych", "wysoki", "pilotaż z pomiarem odsetka odrzuceń; wiek danych w rankingu"],
    ["Czas akceptacji przez apteki w godzinach szczytu", "średni", "SLA w umowie, dźwięk i eskalacja w panelu, metryka per apteka"],
    ["Dokładność GPS w zabudowie (kaniony ulic)", "średni", "progi w konfiguracji, monitoring rozkładu decyzji, ewentualnie map-matching dostawcy"],
    ["Koszt API map przy częstych objazdach", "niski", "limit żądań nowej trasy per dostawa, cache tras apteka–kwartał"],
    ["Czy pokazywać kuriera w drodze do apteki?", "produktowe", "decyzja PM; technicznie to druga faza w tym samym agregacie"],
  ], [4200, 1300, 4138]),
];

// ---------- document ----------

const doc = new Document({
  creator: "Marcin Drozdz",
  title: "Q-commerce dla aptek — Design Document",
  description: "Design document systemu zamówień q-commerce dla aptek",
  styles: {
    default: { document: { run: { font: "Calibri", size: 21, color: "1A1A1A" }, paragraph: { spacing: { line: 276 } } } },
    paragraphStyles: [
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 32, bold: true, color: NAVY, font: "Calibri" },
        paragraph: { spacing: { before: 360, after: 160 }, outlineLevel: 0, keepNext: true } },
      { id: "Heading2", name: "Heading 2", basedOn: "Normal", next: "Normal", quickFormat: true,
        run: { size: 25, bold: true, color: "2E5C8A", font: "Calibri" },
        paragraph: { spacing: { before: 240, after: 100 }, outlineLevel: 1, keepNext: true } },
    ],
  },
  numbering: {
    config: [
      { reference: "bullets", levels: [
        { level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 500, hanging: 260 } } } },
        { level: 1, format: LevelFormat.BULLET, text: "–", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 900, hanging: 260 } } } },
      ] },
      { reference: "steps", levels: [
        { level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 500, hanging: 300 } } } },
      ] },
    ],
  },
  sections: [{
    properties: {
      page: { size: { width: 11906, height: 16838 }, margin: { top: 1134, bottom: 1134, left: 1134, right: 1134 } },
      titlePage: true,
    },
    headers: {
      default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [new TextRun({ text: "Q-commerce dla aptek — Design Document", size: 16, color: MUTED })] })] }),
      first: new Header({ children: [] }),
    },
    footers: {
      default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ children: [PageNumber.CURRENT], size: 18, color: MUTED })] })] }),
      first: new Footer({ children: [] }),
    },
    children: [
      ...title, ...toc, ...summary, ...scope, ...architecture, ...orderFlow, ...pharmacy,
      ...tracking, ...trackingDeep, ...dataApi, ...reliability, ...security, ...plan, ...implemented, ...risks,
    ],
  }],
});

Packer.toBuffer(doc).then((buffer) => {
  fs.writeFileSync(OUT, buffer);
  console.log(`Written ${OUT}`);
});
