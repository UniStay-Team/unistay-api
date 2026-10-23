# Публичный API UniStay v1

Контракт для реализации ASP.NET Core с нуля и подключения существующего Flutter. [OpenAPI baseline](public-api-baseline.yaml) фиксирует поля, вложенность, обязательность и ответы публичных endpoints. Это спецификация совместимости, а не работающий сервер. При расхождении бизнес-правил приоритет имеют [ТЗ](project-plan.md) и ограничения ниже; выявленное расхождение оформляется изменением контракта и проверкой клиентов.

Admin API определён отдельно в [admin-api-v1.md](admin-api-v1.md). В baseline нет административных операций. Генерируемая новым сервером docs/openapi.yaml должна описывать только фактически реализованные endpoints; её нельзя заменять baseline и выдавать за проверенную реализацию.

## Общие правила

Префикс /api/v1/, канонические пути с завершающим /. JSON snake_case. ID — положительное целое, деньги — decimal-строки с двумя знаками, время — UTC ISO 8601, move_in_date — YYYY-MM-DD или null.

Сессия: Authorization: Bearer TOKEN, непрозрачный случайный токен на 24 часа; сервер хранит хеш и срок. Logout/блокировка/смена пароля отзывают сессии. Регистрация не принимает is_admin, is_staff или произвольные permissions.

Список: count (int), next/previous (URL или null), results (массив). Размер — 20, page от 1. Ошибка: error.status (HTTP-код), error.details (строка или объект ошибок полей). Нельзя возвращать HTML, traceback, хеши или токены в ошибке.

400 — валидация; 401 — неверная/истёкшая сессия; 403 — роль/почта; 404 — отсутствующий или недоступный объект; 409 — конфликт; 429 — лимит с Retry-After; 500 — общая ошибка без внутренних деталей.

## Аккаунт и профиль

| Method/path после префикса | Тело | Успех |
| --- | --- | --- |
| POST auth/register/ | email, password, name, role | 201 Me; сессия не выдаётся |
| POST auth/login/ | email, password | 200: token, expires_at, user (Me) |
| POST auth/logout/ | Без тела, сессия обязательна | 204 |
| GET me/ | — | 200 Me |
| GET me/profile/ | — | 200 Profile |
| PATCH me/profile/ | Разрешённые поля Profile, частично | 200 Profile |
| POST auth/verify-email/ | token | 200: detail |
| POST auth/resend-verification/ | email | 200: detail |
| POST auth/password-reset/ | email | 200: detail |
| POST auth/password-reset/confirm/ | uid, token, password | 200: detail |

Me: id, email, name, role, email_verified, profile. Роли student/landlord; административные полномочия проверяются отдельно через admin/me.

Регистрация: уникальный email без учёта регистра, name 1–120, password 10–128 символов с серверной проверкой стойкости. Профиль создаётся в той же транзакции. Повтор email — ошибка; гонка не создаёт второй аккаунт. Проверка дубликата email и неизвестных административных полей обязательна.

Profile: university (до 160), bio (до 1000), schedule (early/late/пустая строка), smoking (bool/null), cleanliness (tidy/relaxed/пустая строка), share_with_group (bool). Пустые текстовые значения допустимы, но заявка требует заполненных university/schedule/cleanliness и smoking != null. false — заполненный ответ. Чужой профиль не изменяется.

Email-подтверждение действует 24 часа. Сброс пароля — 1 час и одноразовый токен; после успеха прежние сессии отзываются. Ответ запроса письма не раскрывает, существует ли аккаунт. В V1 используется локальный тестовый почтовый приёмник. Лимиты: auth 10 запросов/минуту, запрос письма 5/час по клиентскому адресу; проверять их сервером.

## Жильё

| Method/path | Доступ / результат |
| --- | --- |
| GET listings/ | Гость: опубликованные объявления; 200 страница |
| GET listings/{id}/ | Видимое объявление; 200 Listing |
| GET listings/mine/ | Владелец: его объекты; 200 страница |
| POST listings/ | Подтверждённый владелец; 201 Listing со статусом draft |
| PATCH listings/{id}/ | Подтверждённый владелец своего объекта; 200 Listing |
| DELETE listings/{id}/ | Архивирование своего объекта; 204 |
| GET listings/{id}/group/ | Аутентифицированный пользователь с доступом; 200 массив GroupMember |
| POST listings/{id}/photos/ | Подтверждённый владелец; multipart image/caption; 201 Photo |
| DELETE photos/{id}/ | Подтверждённый владелец соответствующего жилья; 204 |

Listing: id, owner, owner_name, title, description, city, district, address, university, price, deposit, utilities_included, capacity, move_in_date, status, photos[], occupied, available, group_ready, created_at, updated_at. Типы, ограничения длины и обязательность входных полей — в baseline.

Photo: id, image, caption. image — URL; клиент разрешает относительный URL относительно API. Владелец берётся из сессии, вычисляемые поля и photos не принимаются в PATCH объявления.

Фильтры: search, city, district, university, min_price, max_price, available, ordering, page. Разрешённая сортировка: price, -price, created_at, -created_at; вторичный ключ id обеспечивает устойчивый порядок. Проверять некорректные числа/границы и сочетание фильтров.

capacity 1–6; occupied — число accepted; available = capacity - occupied; group_ready = available == 0. Цена — за место. Точный address доступен владельцу и принятому студенту, для остальных null. У администратора отдельный защищённый DTO.

Публикация требует хотя бы одно фото. Фото: JPEG/PNG/WebP, до 5 MiB и 20 мегапикселей, максимум 10 на объект; сервер декодирует, удаляет EXIF, перекодирует JPEG. Последнее фото опубликованного объекта не удаляется.

При принятых заявках нельзя менять цену/залог/коммунальные условия/дату; нельзя уменьшать capacity ниже occupied. Конфликт — 409. Архивирование не стирает заявки и историю.

## Заявки и соседи

| Method/path | Тело / результат |
| --- | --- |
| GET applications/ | Студент: свои заявки; владелец: заявки на своё жильё. 200 страница |
| POST applications/ | listing_id, message (до 1000, необязателен); 201 Application |
| GET applications/{id}/ | Своя заявка / владелец объекта; 200 Application |
| POST applications/{id}/accept/ | Только владелец; без тела; 200 Application |
| POST applications/{id}/reject/ | Только владелец; без тела; 200 Application |
| POST applications/{id}/cancel/ | Только студент-автор; без тела; 200 Application |

Application: id, listing_id, listing_title, student, status, message, created_at, updated_at, events[].
student: id, name, profile; email в этом DTO отсутствует.
Event: from_status, to_status, created_at, actor_id; первый from_status — пустая строка.

Подать заявку может подтверждённый студент с заполненным профилем на опубликованное жильё со свободным местом. До 5 pending, одна активная заявка на объект, не более одного accepted на студента. Дубликаты и конфликт перехода — 409.

pending → accepted/rejected/cancelled; accepted → cancelled. Принятие занимает место, отмена accepted освобождает его. Повтор уже выполненного действия не создаёт повторное событие. Изменение состояния и запись истории атомарны; конкурентное принятие проверяется на PostgreSQL.

GroupMember: id, name, university, bio, schedule, smoking, cleanliness, compatibility, matching_preferences[], compared_preferences. Видимость анкеты соседа определяется согласием share_with_group; собственный профиль и доступ владельца сохраняются. Email не выдаётся. Совместимость — округлённый процент совпадений по известным schedule/smoking/cleanliness; без общих ответов null, причина и число сравнений показываются пользователю.

## Минимум D0 и проверка

D0 реализует только перечисленные в разделе 3 ТЗ маршруты входа, чтения профиля/каталога, создания/чтения заявки и три административных списка. Полный контракт задаёт V1 и не означает готовность всего объёма сегодня.

Алмас сверяет DTO и ошибки с Flutter; Турар проверяет реальные запросы клиента; Талант — административные DTO. Готовность D0: Q01–Q06. Полная версия: T01–T12, включая права, транзакции и оба клиента.
