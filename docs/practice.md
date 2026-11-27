# Работы 1–29

Индивидуальный проект, вариант 17. Задачи и этапы находятся в [Issues](https://github.com/CyberSecArtur/uchebka/issues?q=is%3Aissue), проверки в [Actions](https://github.com/CyberSecArtur/uchebka/actions), публикации в [Releases](https://github.com/CyberSecArtur/uchebka/releases).

| № | Результат |
| --- | --- |
| 1 | [Требования, роли, сценарии и жизненный цикл](requirements.md). |
| 2 | [Модульная структура и зависимости](architecture.md). |
| 3 | [Перечень артефактов](artifacts.md) и [13 маршрутов API](api-spec.md). |
| 4 | Git, история, правила исключения базы, сборок и результатов тестов. |
| 5 | Ветки main, develop, feature и release. [Правила ветвления](../README.md#правила-работы-с-ветками). |
| 6 | Задачи, этапы, метки, [шаблон дефекта](../.github/ISSUE_TEMPLATE/bug_report.md). |
| 7 | [Решение из шести проектов](../StockDesk.sln). |
| 8 | [Ядро и правила смены статусов](../StockDesk.Core/Domain.cs). |
| 9 | [Миграция SQLite и индексы](../StockDesk.Data/CleanDb.cs), [начальные данные](../StockDesk.Api/DemoData.cs). |
| 10 | Minimal API и [контракт маршрутов](api-spec.md). |
| 11 | Обработчик ошибок в API и [описание кодов ответов](api-spec.md). |
| 12 | [Импорт CSV и три формата выгрузки](artifacts.md). |
| 13 | [WPF, формы и навигация](user-guide.md). |
| 14 | [Вход, JWT и проверки прав](security.md). |
| 15 | Консольный журнал API и диагностика времени обновления WPF. Файловый Serilog не подключён. |
| 16 | Предварительный выпуск и [проверка интеграции](test-cases.md). Парная проверка пропущена. |
| 17 | Распределение по статусам и суммы отгрузок за шесть месяцев в «Отчётности». |
| 18 | DebuggerDisplay и Debug.Assert для заявки, Stopwatch для обновления данных. |
| 19 | Проверка состояния через /health, сообщения входа и запуска в [диагностике](security.md). |
| 20 | [Модульные тесты Core](../StockDesk.Tests/DomainTests.cs). |
| 21 | [Интеграционные тесты API](../StockDesk.Tests/ApiTests.cs). |
| 22 | [Тесты представления WPF](../StockDesk.UiTests/PresentationTests.cs), [ручные сценарии](test-cases.md). |
| 23 | [Тест-кейсы и связь с требованиями](test-cases.md). |
| 24 | Ключевые проверки ошибок и прав описаны в [диагностике](security.md). Рецензирование с партнёром пропущено. |
| 25 | В сборке задействованы анализаторы .NET SDK и [.editorconfig](../.editorconfig). |
| 26 | Проекции EF Core, чтение без отслеживания, индексы по статусу и исполнителю. Нагрузочные замеры не проводились. |
| 27 | [Матрица совместимости](compatibility.md) с отметками о фактической проверке. |
| 28 | [Управление доступом и валидация](security.md). |
| 29 | [CI](../.github/workflows/ci.yml), [CHANGELOG](../CHANGELOG.md), [руководство](user-guide.md) и [выпуски](https://github.com/CyberSecArtur/uchebka/releases). |
