# Pull Request

## Description
Implements the full CRUD API for business categories (#55): `GET`/`POST /business/categories` and `PATCH`/`DELETE /business/categories/{categoryId}`. Categories use a single self-referencing table already migrated on `develop` (#87) — this PR is the application layer on top of it: controller, services, repository, business-rule validation for the single-level hierarchy, and the JWT/`SUPERADMIN` wiring `Spot.Business.Api` didn't have yet.

## Related Issue
Closes #55

## Changes Included

### Backend Changes
- [x] Added JWT validation to `Spot.Business.Api` (`AddSpotJwtAuthentication`) — it had none before this PR, needed since create/update/delete require `SUPERADMIN`
- [x] Added the standard `{ code, message, timestamp }` error shape (`ConfigureApiBehaviorOptions`) and a global exception handler, matching every other microservice
- [x] Added new endpoints: `GET /business/categories` (public), `POST /business/categories`, `PATCH /business/categories/{categoryId}`, `DELETE /business/categories/{categoryId}` (all three require `SUPERADMIN`)
- [x] Added repository (`ICategoryRepository`/`CategoryRepository`) and services (`ICategoryService`/`CategoryService`)
- [x] Added DTOs: `CategoryDto`, `CategoryCreateRequest`, `CategoryUpdateRequest`, `CategoryFilterQuery`, plus `Optional<T>` (ported from `Spot.Auth.Api`, kept local — a cross-service move into `Spot.Shared` needs its own flagging) for exact PATCH semantics on `parentCategoryId`/`description`
- [x] Business rules enforced: parent must exist and be a root category (a subcategory can't be used as a parent — the hierarchy is capped at one level), a category can't be its own parent, a category with existing subcategories can't become a subcategory itself, `(parentCategoryId, name)` must be unique, and deleting a category with subcategories is rejected (never cascades) — each with an up-front check plus a DB-constraint catch as a race-condition safety net (same pattern as `Spot.Auth.Api`'s `UserRepository`)
- [x] Added unit tests (`CategoryServiceTests`, 24 cases) and repository tests (`CategoryRepositoryTests`, 15 cases, EF InMemory) covering every business rule
- [x] Added integration tests (`CategoriesControllerTests`, 24 cases) via `WebApplicationFactory<Program>`, exercising the real pipeline — routing, JWT auth, model validation, and the real `CategoryService` — with only the repository faked
- [x] New test project `Spot.Business.Api.Tests`, registered in `spot-backend.slnx` from the start (CI discovers test projects independently of the `.slnx`, but the `.slnx`-driven build step doesn't — an unregistered project silently never gets built, and then `dotnet test --no-build` on it exits 0 with zero tests run and no error, so CI stays green while running nothing)
- [x] Wired `Spot.Business.Api`'s Docker image/Compose service to validate JWTs (public key secret, `entrypoint-jwt.sh`), matching `auth-api`/`aisearch-api`
- [x] Updated `README.md`'s JWT section (previously said Business.Api was still a "future" consumer) and `Spot.Business.Api.http` with real request examples
- [ ] Other:

### Out of scope (per the ticket, and per `develop`)
- Assigning categories to businesses/services — separate ticket
- Multiple levels of nesting, category images/metadata, ordering, translations — explicitly out of scope
- `contracts/spot-api.yaml`'s existing `AuthDbContext`/EF migration for `categories` — already on `develop` (#87), untouched here

## Architecture
```
CategoriesController → ICategoryService (CategoryService) → ICategoryRepository (CategoryRepository) → BusinessDbContext (EF Core / Npgsql) → PostgreSQL
```
`GET` has no `[Authorize]` (public per the contract); `POST`/`PATCH`/`DELETE` use `[Authorize(Roles = "SUPERADMIN")]` per-action rather than at the controller level, same reasoning as `AuthController` (a public action can't sit under a class-level `[Authorize]`). Business-rule violations are custom exceptions (`CategoryNotFoundException` → 404, `InvalidCategoryHierarchyException` → 400, `DuplicateCategoryException`/`CategoryHasSubcategoriesException` → 409) caught in the controller and mapped to the contract's `Error` shape, the same try/catch-per-exception-type style already used in `AuthController.Register`.

## API / Data Changes
- `GET /business/categories` — already fully defined in `contracts/spot-api.yaml`; implemented as specified (pagination, `parentCategoryId` filter with `null`/absent/id semantics).
- `POST`/`PATCH /business/categories/{categoryId}` — already defined; **added the missing `404` response to `POST`'s contract entry**, since the ticket's acceptance criteria require it (nonexistent parent) but the contract hadn't declared it.
- **`DELETE /business/categories/{categoryId}` — new contract addition.** It wasn't in `contracts/spot-api.yaml` at all before this PR; added with `204`/`401`/`403`/`404`/`409`/`500` responses, `operationId: deleteCategory`.
- No new migrations — `categories` and its unique `(parent_category_id, name)` index / `ON DELETE RESTRICT` FK already exist on `develop` via #87.
- `Spot.Shared` **not touched** by this PR (only consumed existing `PaginatedResponse<T>`/`PaginationMeta`/`ApiError`/`AddSpotJwtAuthentication`).

## Evidence
- `dotnet build --configuration Release` and `Debug` — clean, 0 warnings, 0 errors.
- `dotnet test` (all 3 test projects, both configurations) — **132/132 passing** (63 new `Spot.Business.Api.Tests` + 65 `Spot.Auth.Api.Tests` + 4 `Spot.Shared.Tests`), reproducing `.github/workflows/tests.yml`'s own discovery/run steps exactly.
- All four endpoints manually verified against a real Postgres instance via Docker Compose, both hitting `business-api` directly and through the Gateway (`http://localhost:5195/api/v1/business/categories`) — every listed status code (200/201/204/400/401/403/404/409) reproduced with a real `SUPERADMIN`/`CLIENT` JWT minted by `auth-api`'s dev endpoint, including the two DB-level safety nets (duplicate name race, delete-with-children via the real `ON DELETE RESTRICT` constraint).

## Notes
No frontend work included; `spot-frontend` can now build the category management screens against a real, deployed contract.
