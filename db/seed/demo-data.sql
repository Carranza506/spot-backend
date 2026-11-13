-- Demo data for local development and the project defense (#157). Applied by the `seed` service in
-- docker-compose.yml after the migrations. Idempotent: every insert skips rows that already exist,
-- so running it again never duplicates or overwrites anything. Reset with `docker compose down -v`.
--
-- All accounts share the password "Spot1234!" (documented in README.md > "Demo data"). The hash is
-- ASP.NET Core Identity's PasswordHasher V3 format (PBKDF2-HMACSHA512, 100000 iterations), the
-- same one Spot.Auth.Api writes on register, so these accounts log in like any other.
--
-- Booking dates are relative to the day the seed first runs (Costa Rica time): reset the volume
-- before a demo so "upcoming" bookings are still upcoming.

BEGIN;

-- ---------- Accounts ----------
INSERT INTO users (email, password_hash, first_name, last_name, phone, role)
VALUES
  ('cliente@spot.test',   'AQAAAAIAAYagAAAAECuBFkArhltaMFHCda6E5HWbr8y3El5oDMjOsiE75b5zdl8F5KgXoSntVXn4UM4WAg==', 'María', 'Rodríguez', '+50688881234', 'client'),
  ('negocio@spot.test',   'AQAAAAIAAYagAAAAECuBFkArhltaMFHCda6E5HWbr8y3El5oDMjOsiE75b5zdl8F5KgXoSntVXn4UM4WAg==', NULL, NULL, '+50622601000', 'business'),
  ('negocio2@spot.test',  'AQAAAAIAAYagAAAAECuBFkArhltaMFHCda6E5HWbr8y3El5oDMjOsiE75b5zdl8F5KgXoSntVXn4UM4WAg==', NULL, NULL, '+50622602000', 'business'),
  ('negocio3@spot.test',  'AQAAAAIAAYagAAAAECuBFkArhltaMFHCda6E5HWbr8y3El5oDMjOsiE75b5zdl8F5KgXoSntVXn4UM4WAg==', NULL, NULL, '+50622603000', 'business'),
  ('admin@spot.test',     'AQAAAAIAAYagAAAAECuBFkArhltaMFHCda6E5HWbr8y3El5oDMjOsiE75b5zdl8F5KgXoSntVXn4UM4WAg==', 'Admin', 'Spot', NULL, 'superadmin')
ON CONFLICT (email) DO NOTHING;

-- ---------- Businesses, near the UNA campus in Heredia ----------
-- Their root categories (Belleza, Salud, Deportes) come from Spot.Business.Api's migrations.
CREATE TEMP TABLE demo_business (owner text, slug text, name text, description text, phone text,
  category text, address text, lat double precision, lng double precision) ON COMMIT DROP;
INSERT INTO demo_business VALUES
  ('negocio@spot.test',  'barberia-la-u', 'Barbería La U',
   'Cortes clásicos y modernos, arreglo de barba y tratamientos capilares a pasos de la UNA.', '+50622601000',
   'Belleza', '100 m norte de la entrada principal de la UNA', 9.9996, -84.1103),
  ('negocio2@spot.test', 'spa-bienestar-heredia', 'Spa Bienestar Heredia',
   'Masajes relajantes y terapéuticos, faciales y tratamientos de bienestar.', '+50622602000',
   'Salud', 'Costado sur del Parque Central de Heredia', 9.9980, -84.1172),
  ('negocio3@spot.test', 'gimnasio-fuerza-norte', 'Gimnasio Fuerza Norte',
   'Entrenamiento funcional, clases grupales y entrenamiento personalizado.', '+50622603000',
   'Deportes', '300 m este del Mall Paseo de las Flores', 10.0021, -84.1150);

INSERT INTO businesses (account_id, name, slug, description, email, phone)
SELECT u.id, d.name, d.slug, d.description, d.owner, d.phone
FROM demo_business d JOIN users u ON u.email = d.owner
WHERE NOT EXISTS (SELECT 1 FROM businesses b WHERE b.slug = d.slug OR b.account_id = u.id);

INSERT INTO business_locations (business_id, address, city, province, country, location)
SELECT b.id, d.address, 'Heredia', 'Heredia', 'Costa Rica',
       ST_SetSRID(ST_MakePoint(d.lng, d.lat), 4326)::geography
FROM demo_business d JOIN businesses b ON b.slug = d.slug
WHERE NOT EXISTS (SELECT 1 FROM business_locations l WHERE l.business_id = b.id);

INSERT INTO business_categories (business_id, category_id)
SELECT b.id, c.id
FROM demo_business d
JOIN businesses b ON b.slug = d.slug
JOIN categories c ON c.parent_category_id IS NULL AND c.name = d.category
WHERE NOT EXISTS (SELECT 1 FROM business_categories bc WHERE bc.business_id = b.id AND bc.category_id = c.id);

-- Monday to Saturday 08:00-18:00, Sunday closed (day_of_week follows .NET's DayOfWeek: 0 = Sunday).
INSERT INTO business_hours (business_id, day_of_week, open_time, close_time, is_closed)
SELECT b.id, dow,
       CASE WHEN dow = 0 THEN NULL ELSE time '08:00' END,
       CASE WHEN dow = 0 THEN NULL ELSE time '18:00' END,
       dow = 0
FROM demo_business d
JOIN businesses b ON b.slug = d.slug
CROSS JOIN generate_series(0, 6) AS dow
ON CONFLICT (business_id, day_of_week) DO NOTHING;

-- ---------- Services ----------
INSERT INTO services (business_id, name, description, price, duration_minutes)
SELECT b.id, s.name, s.description, s.price, s.minutes
FROM (VALUES
  ('barberia-la-u', 'Corte de cabello', 'Corte a tijera o máquina, con lavado.', 7000, 45),
  ('barberia-la-u', 'Arreglo de barba', 'Perfilado y toalla caliente.', 4500, 30),
  ('barberia-la-u', 'Corte + barba', 'Corte de cabello y arreglo de barba.', 10500, 60),
  ('spa-bienestar-heredia', 'Masaje relajante', 'Masaje de cuerpo completo con aceites.', 25000, 60),
  ('spa-bienestar-heredia', 'Facial hidratante', 'Limpieza profunda e hidratación.', 18000, 45),
  ('spa-bienestar-heredia', 'Masaje descontracturante', 'Espalda, cuello y hombros.', 20000, 45),
  ('gimnasio-fuerza-norte', 'Entrenamiento personalizado', 'Sesión uno a uno con entrenador.', 12000, 60),
  ('gimnasio-fuerza-norte', 'Clase funcional', 'Clase grupal de entrenamiento funcional.', 5000, 45),
  ('gimnasio-fuerza-norte', 'Evaluación física', 'Medidas, composición corporal y plan.', 8000, 30)
) AS s(slug, name, description, price, minutes)
JOIN businesses b ON b.slug = s.slug
WHERE NOT EXISTS (SELECT 1 FROM services x WHERE x.business_id = b.id AND x.name = s.name);

-- ---------- Bookings for the demo client at Barbería La U ----------
-- Fixed ids, so a second run finds them. Times are on the 15-minute grid, in Costa Rica time.
INSERT INTO bookings (id, user_id, business_id, service_id, start_at, end_at, status,
                      service_name, service_price, service_duration_minutes, notes)
SELECT bk.id, u.id, b.id, s.id,
       (date_trunc('day', now() AT TIME ZONE 'America/Costa_Rica') + bk.start_offset) AT TIME ZONE 'America/Costa_Rica',
       (date_trunc('day', now() AT TIME ZONE 'America/Costa_Rica') + bk.start_offset + make_interval(mins => s.duration_minutes)) AT TIME ZONE 'America/Costa_Rica',
       bk.status::booking_status, s.name, s.price, s.duration_minutes, bk.notes
FROM (VALUES
  ('5eed0000-0000-4000-8000-000000000001'::uuid, interval '1 day 10 hours',  'pending',   'Primera vez, corte corto.'),
  ('5eed0000-0000-4000-8000-000000000002'::uuid, interval '2 days 9 hours',  'confirmed', NULL),
  ('5eed0000-0000-4000-8000-000000000003'::uuid, interval '-2 days 15 hours', 'completed', NULL),
  ('5eed0000-0000-4000-8000-000000000004'::uuid, interval '-1 day 11 hours', 'cancelled', NULL)
) AS bk(id, start_offset, status, notes)
JOIN users u ON u.email = 'cliente@spot.test'
JOIN businesses b ON b.slug = 'barberia-la-u'
JOIN services s ON s.business_id = b.id AND s.name = 'Corte de cabello'
ON CONFLICT (id) DO NOTHING;

-- A review on the completed booking, so Reseñas has something to show.
INSERT INTO reviews (booking_id, user_id, business_id, rating, comment)
SELECT bk.id, bk.user_id, bk.business_id, 5, 'Excelente atención y muy puntuales. ¡Volveré!'
FROM bookings bk
WHERE bk.id = '5eed0000-0000-4000-8000-000000000003'
ON CONFLICT (booking_id) DO NOTHING;

COMMIT;
