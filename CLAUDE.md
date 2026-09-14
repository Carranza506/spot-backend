# Contexto y flujo de trabajo — Spot Backend

Este documento aplica al repo `spot-backend`: microservicios en .NET 10 coordinados por un API Gateway, con PostgreSQL 18 como base de datos. Aplica tanto para personas del equipo (Jeremy, Dorian, Carranza, u otros) como para asistentes de IA que tomen un issue de este repo.

> **Antes de empezar cualquier trabajo**, si quien ejecuta el issue es un asistente de IA, debe **preguntar explícitamente**:
> 1. ¿Qué item/issue se va a trabajar? (número o link del issue)
> 2. ¿De qué usuario es ese issue? (Jeremy, Dorian, Carranza, etc.)
>
> No asumir el item ni el usuario por defecto — siempre confirmarlo antes de tocar código.

## 1. Alcance: solo este repo

- Este documento aplica únicamente a `spot-backend`. Si el issue es de frontend, no corresponde tocar código acá.
- Un issue de backend se resuelve completo dentro de `spot-backend`. Si durante el trabajo aparece una necesidad del lado frontend, **no se implementa acá**: se anota como dependencia para que se cree un issue aparte en `spot-frontend`.

## 2. Dónde están las tareas

- Los issues están en **GitHub Issues de este repo** (`spot-backend`).
- Se trabajan según a quién estén asignados — el responsable puede variar (Jeremy, Dorian, Carranza, etc.).
- Leer la descripción completa, criterios de aceptación y comentarios antes de empezar.

## 3. Estructura del proyecto (dónde tocar qué)

```
src/
  Spot.Gateway/             → API Gateway, punto de entrada único
  Spot.Auth.Api/            → Autenticación y gestión de usuarios
  Spot.Business.Api/        → Negocios, servicios y horarios
  Spot.Booking.Api/         → Disponibilidad y reservas
  Spot.AiSearch.Api/        → Búsqueda inteligente (integración IA)
  Spot.Notifications.Api/   → Notificaciones push
  Spot.Shared/               → Librería compartida (DTOs, enums, contratos entre servicios)
```

- Identificar primero a qué microservicio pertenece el issue antes de escribir código.
- **`Spot.Shared` es sensible**: un cambio ahí afecta a varios microservicios a la vez. Si el issue lo requiere, avisar al equipo antes de mergear (regla ya definida en el README del repo).

## 4. Antes de escribir código

1. **Buscar lógica/servicios reutilizables ya existentes** dentro del microservicio correspondiente (y en `Spot.Shared` si aplica) antes de crear algo nuevo. Si ya existe algo similar, seguir la misma estructura y convenciones — no reinventar.
2. **Revisar siempre `contracts/spot-api.yaml`** (en la raíz de este repo) antes de tocar cualquier endpoint:
   - Verificar si el endpoint ya existe (evitar duplicar rutas).
   - Verificar el contrato exacto: parámetros, request/response, códigos de error.
   - Si se necesita un endpoint nuevo o modificar uno existente, **actualizar `spot-api.yaml` como parte del mismo PR** — este archivo es el que consume `spot-frontend`, así que debe reflejar siempre la realidad del código.

## 5. Durante el desarrollo

- Rama: `feature/nombre-corto` o `fix/nombre-corto`.
- Commits cortos, descriptivos, en inglés.
- Mantener el estilo de código y patrones ya usados en el microservicio.
- No hacer refactors fuera del alcance del issue sin confirmarlo antes.
- **No hacer commits ni push automáticamente** — siempre esperar confirmación explícita del usuario antes de ejecutar cualquier `git commit` o `git push`.

## 6. Al terminar

- Abrir un Pull Request usando la plantilla del repositorio.
- Vincular el PR al issue (`Closes #123`).
- Se requiere **al menos 1 revisor** antes de mergear a `main`.
- Si se modificó `Spot.Shared` o `spot-api.yaml`, dejarlo explícito en el PR y avisar al equipo.

## 7. Checklist rápido

- [ ] Confirmé el item y el usuario responsable
- [ ] Confirmé que el issue es de backend (no de frontend)
- [ ] Identifiqué el microservicio correspondiente
- [ ] Revisé lógica reutilizable existente (incluyendo `Spot.Shared`)
- [ ] Revisé `contracts/spot-api.yaml`
- [ ] Actualicé `spot-api.yaml` si el cambio tocaba la API
- [ ] Si toqué `Spot.Shared`, avisé al equipo
- [ ] Abrí el PR con la plantilla y lo vinculé al issue
- [ ] Conseguí al menos 1 aprobación antes de mergear a `main`
