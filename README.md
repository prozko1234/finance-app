# finance-app

Фінансовий менеджер для людей, що живуть між валютами, і для мізків, які ненавидять
ручний облік. Головна ідея — **одна цифра: скільки безпечно витратити сьогодні**.

Фаза 1 (MVP для себе): ручний ввід, мультивалюта, safe-to-spend. Локально, без банк-синку.
Продуктовий контекст і рішення — в Obsidian vault (`02-Projects/finance-app/`).

## Стек
- **Бекенд:** ASP.NET Core (.NET 10), Minimal API, EF Core + SQLite
- **Фронтенд:** React + TypeScript + Vite (PWA)
- **FX:** NBP API (офіційний PLN) + ECB фолбек
- **Тести:** xUnit

## Структура
```
backend/     .NET solution (Api, Domain, Infrastructure, Api.Tests)
frontend/    React + TS + Vite PWA
```

## Локальний запуск
Бекенд:
```bash
cd backend && dotnet run --project Api
```
Фронтенд (окремий термінал):
```bash
cd frontend && npm install && npm run dev
```

## Тести
```bash
cd backend && dotnet test
```

## Версіонування
- Гілки: trunk-based, короткі `feat/*` для більших змін.
- Коміти: conventional commits (`feat:`, `fix:`, `chore:`).
- Схема БД: EF Core міграції (в git).
- Релізи: SemVer теги (`v0.1.0` — старт самокористування).
