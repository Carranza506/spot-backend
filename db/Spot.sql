CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TYPE user_role AS ENUM ('CLIENT','BUSINESS_OWNER','SUPERADMIN');
CREATE TYPE contact_type AS ENUM ('PHONE','WHATSAPP','EMAIL','WEBSITE','FACEBOOK','INSTAGRAM','TIKTOK','OTHER');
CREATE TYPE booking_status AS ENUM ('PENDING','CONFIRMED','COMPLETED','CANCELLED','NO_SHOW');
CREATE TYPE ai_request_status AS ENUM ('SUCCESS','ERROR','TIMEOUT');
CREATE TYPE ai_request_type AS ENUM ('BUSINESS_SEARCH','GENERAL_QUERY','OTHER');
CREATE TYPE auth_provider AS ENUM ('GOOGLE');


CREATE OR REPLACE FUNCTION set_updated_at() RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN NEW.updated_at=CURRENT_TIMESTAMP; RETURN NEW; END; $$;

CREATE TABLE users (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), email VARCHAR(255) NOT NULL UNIQUE,
 password_hash TEXT, first_name VARCHAR(100) NOT NULL, last_name VARCHAR(100) NOT NULL,
 phone VARCHAR(30), profile_photo_url TEXT, role user_role NOT NULL DEFAULT 'CLIENT',
 is_active BOOLEAN NOT NULL DEFAULT TRUE, created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_users_role ON users(role);
CREATE TRIGGER trg_users_updated_at BEFORE UPDATE ON users FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE user_auth_providers (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
 provider auth_provider NOT NULL, provider_user_id VARCHAR(255) NOT NULL,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 UNIQUE(provider,provider_user_id), UNIQUE(user_id,provider));
CREATE INDEX idx_auth_providers_user ON user_auth_providers(user_id);

CREATE TABLE refresh_tokens (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
 token_hash TEXT NOT NULL UNIQUE, expires_at TIMESTAMPTZ NOT NULL, revoked_at TIMESTAMPTZ,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_refresh_tokens_user ON refresh_tokens(user_id);

CREATE TABLE categories (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), parent_category_id UUID REFERENCES categories(id) ON DELETE RESTRICT,
 name VARCHAR(100) NOT NULL, description TEXT, is_active BOOLEAN NOT NULL DEFAULT TRUE,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 UNIQUE(parent_category_id,name));
CREATE INDEX idx_categories_parent ON categories(parent_category_id);
CREATE TRIGGER trg_categories_updated_at BEFORE UPDATE ON categories FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE businesses (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), name VARCHAR(150) NOT NULL, slug VARCHAR(180) NOT NULL UNIQUE,
 description TEXT, legal_name VARCHAR(200), email VARCHAR(255), phone VARCHAR(30), website TEXT, logo_url TEXT,
 is_active BOOLEAN NOT NULL DEFAULT TRUE, created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_businesses_name ON businesses(name);
CREATE TRIGGER trg_businesses_updated_at BEFORE UPDATE ON businesses FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE business_owners (
 business_id UUID NOT NULL REFERENCES businesses(id) ON DELETE CASCADE,
 user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 PRIMARY KEY(business_id,user_id));
CREATE INDEX idx_business_owners_user ON business_owners(user_id);

CREATE TABLE business_categories (
 business_id UUID NOT NULL REFERENCES businesses(id) ON DELETE CASCADE,
 category_id UUID NOT NULL REFERENCES categories(id) ON DELETE RESTRICT,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 PRIMARY KEY(business_id,category_id));
CREATE INDEX idx_business_categories_category ON business_categories(category_id);

CREATE TABLE business_locations (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), business_id UUID NOT NULL UNIQUE REFERENCES businesses(id) ON DELETE CASCADE,
 address TEXT NOT NULL, city VARCHAR(100), province VARCHAR(100), country VARCHAR(100) NOT NULL DEFAULT 'Costa Rica',
 postal_code VARCHAR(20), location GEOGRAPHY(POINT,4326) NOT NULL,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_business_locations_geo ON business_locations USING GIST(location);
CREATE TRIGGER trg_business_locations_updated_at BEFORE UPDATE ON business_locations FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE business_contacts (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), business_id UUID NOT NULL REFERENCES businesses(id) ON DELETE CASCADE,
 type contact_type NOT NULL, value TEXT NOT NULL, is_primary BOOLEAN NOT NULL DEFAULT FALSE,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_business_contacts_business ON business_contacts(business_id);

CREATE TABLE services (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), business_id UUID NOT NULL REFERENCES businesses(id) ON DELETE CASCADE,
 name VARCHAR(150) NOT NULL, description TEXT, price NUMERIC(12,2) NOT NULL CHECK(price>=0),
 duration_minutes INTEGER NOT NULL CHECK(duration_minutes>0), is_active BOOLEAN NOT NULL DEFAULT TRUE,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_services_business ON services(business_id);
CREATE TRIGGER trg_services_updated_at BEFORE UPDATE ON services FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE business_photos (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), business_id UUID NOT NULL REFERENCES businesses(id) ON DELETE CASCADE,
 storage_key TEXT NOT NULL, url TEXT, is_primary BOOLEAN NOT NULL DEFAULT FALSE, display_order INTEGER NOT NULL DEFAULT 0 CHECK(display_order>=0),
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_business_photos_business ON business_photos(business_id);

CREATE TABLE service_photos (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), service_id UUID NOT NULL REFERENCES services(id) ON DELETE CASCADE,
 storage_key TEXT NOT NULL, url TEXT, display_order INTEGER NOT NULL DEFAULT 0 CHECK(display_order>=0),
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_service_photos_service ON service_photos(service_id);

CREATE TABLE business_hours (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), business_id UUID NOT NULL REFERENCES businesses(id) ON DELETE CASCADE,
 day_of_week SMALLINT NOT NULL CHECK(day_of_week BETWEEN 0 AND 6), open_time TIME, close_time TIME,
 is_closed BOOLEAN NOT NULL DEFAULT FALSE, created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 UNIQUE(business_id,day_of_week),
 CHECK(is_closed OR (open_time IS NOT NULL AND close_time IS NOT NULL AND open_time<close_time)));
CREATE INDEX idx_business_hours_business ON business_hours(business_id);
CREATE TRIGGER trg_business_hours_updated_at BEFORE UPDATE ON business_hours FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE business_schedule_exceptions (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), business_id UUID NOT NULL REFERENCES businesses(id) ON DELETE CASCADE,
 exception_date DATE NOT NULL, is_closed BOOLEAN NOT NULL DEFAULT FALSE, open_time TIME, close_time TIME, reason VARCHAR(255),
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 UNIQUE(business_id,exception_date),
 CHECK(is_closed OR (open_time IS NOT NULL AND close_time IS NOT NULL AND open_time<close_time)));
CREATE INDEX idx_schedule_exceptions_business_date ON business_schedule_exceptions(business_id,exception_date);
CREATE TRIGGER trg_schedule_exceptions_updated_at BEFORE UPDATE ON business_schedule_exceptions FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE bookings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id),
    business_id UUID NOT NULL REFERENCES businesses(id),
    service_id UUID NOT NULL REFERENCES services(id),
    start_at TIMESTAMPTZ NOT NULL,
    end_at TIMESTAMPTZ NOT NULL,
    status booking_status NOT NULL DEFAULT 'PENDING',
    service_name VARCHAR(255) NOT NULL,
    service_price NUMERIC(10,2) NOT NULL,
    service_duration_minutes INTEGER NOT NULL,
    notes TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_booking_dates
        CHECK (start_at < end_at),
    CONSTRAINT chk_booking_service_price
        CHECK (service_price >= 0),
    CONSTRAINT chk_booking_service_duration
        CHECK (service_duration_minutes > 0)
);
CREATE INDEX idx_bookings_user_start ON bookings(user_id,start_at);
CREATE INDEX idx_bookings_business_start ON bookings(business_id,start_at);
CREATE INDEX idx_bookings_service ON bookings(service_id);
CREATE TRIGGER trg_bookings_updated_at BEFORE UPDATE ON bookings FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE favorite_businesses (
 user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
 business_id UUID NOT NULL REFERENCES businesses(id) ON DELETE CASCADE,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
 PRIMARY KEY(user_id,business_id));

CREATE TABLE reviews (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), booking_id UUID NOT NULL UNIQUE REFERENCES bookings(id) ON DELETE RESTRICT,
 user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT, business_id UUID NOT NULL REFERENCES businesses(id) ON DELETE RESTRICT,
 rating SMALLINT NOT NULL CHECK(rating BETWEEN 1 AND 5), comment TEXT,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_reviews_business ON reviews(business_id);
CREATE TRIGGER trg_reviews_updated_at BEFORE UPDATE ON reviews FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE TABLE ai_requests (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), user_id UUID REFERENCES users(id) ON DELETE SET NULL,
 provider VARCHAR(50) NOT NULL, model VARCHAR(100) NOT NULL,
 request_type ai_request_type NOT NULL DEFAULT 'OTHER', prompt TEXT NOT NULL, response TEXT,
 extracted_parameters JSONB, tool_calls JSONB, status ai_request_status NOT NULL,
 input_tokens INTEGER CHECK(input_tokens IS NULL OR input_tokens>=0), output_tokens INTEGER CHECK(output_tokens IS NULL OR output_tokens>=0),
 total_tokens INTEGER CHECK(total_tokens IS NULL OR total_tokens>=0), latency_ms INTEGER CHECK(latency_ms IS NULL OR latency_ms>=0),
 error_code VARCHAR(100), error_message TEXT, ip_address INET, user_agent TEXT,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_ai_requests_user_created ON ai_requests(user_id,created_at DESC);
CREATE INDEX idx_ai_requests_parameters ON ai_requests USING GIN(extracted_parameters);

CREATE TABLE audit_logs (
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(), user_id UUID REFERENCES users(id) ON DELETE SET NULL,
 action VARCHAR(50) NOT NULL, entity_type VARCHAR(100) NOT NULL, entity_id UUID,
 old_values JSONB, new_values JSONB, ip_address INET, user_agent TEXT,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX idx_audit_logs_user_created ON audit_logs(user_id,created_at DESC);
CREATE INDEX idx_audit_logs_entity ON audit_logs(entity_type,entity_id);
CREATE INDEX idx_audit_logs_created ON audit_logs(created_at DESC);

ALTER TABLE bookings ADD CONSTRAINT excl_active_booking_overlap
EXCLUDE USING GIST (business_id WITH =, tstzrange(start_at,end_at,'[)') WITH &&)
WHERE(status IN ('PENDING','CONFIRMED'));

CREATE OR REPLACE FUNCTION validate_review() RETURNS TRIGGER LANGUAGE plpgsql AS $$
DECLARE b RECORD;
BEGIN
 SELECT user_id,business_id,status INTO b FROM bookings WHERE id=NEW.booking_id;
 IF NOT FOUND THEN RAISE EXCEPTION 'Booking % does not exist',NEW.booking_id; END IF;
 IF b.status<>'COMPLETED' THEN RAISE EXCEPTION 'A review can only be created for a completed booking'; END IF;
 IF b.user_id<>NEW.user_id THEN RAISE EXCEPTION 'Review user does not match booking user'; END IF;
 IF b.business_id<>NEW.business_id THEN RAISE EXCEPTION 'Review business does not match booking business'; END IF;
 RETURN NEW;
END; $$;
CREATE TRIGGER trg_validate_review BEFORE INSERT OR UPDATE ON reviews FOR EACH ROW EXECUTE FUNCTION validate_review();

CREATE OR REPLACE FUNCTION generate_slug(input_text TEXT) RETURNS TEXT LANGUAGE plpgsql AS $$
DECLARE result TEXT;
BEGIN
 result:=lower(trim(input_text));
 result:=translate(result,'áéíóúüñÁÉÍÓÚÜÑ','aeiouunAEIOUUN');
 result:=regexp_replace(result,'[^a-zA-Z0-9]+','-','g');
 result:=regexp_replace(result,'(^-+|-+$)','','g');
 RETURN result;
END; $$;

INSERT INTO categories(name,description) VALUES
 ('Belleza','Servicios relacionados con belleza y cuidado personal'),
 ('Salud','Servicios relacionados con salud y bienestar'),
 ('Deportes','Servicios e instalaciones deportivas'),
 ('Restaurantes','Restaurantes y establecimientos de comida'),
 ('Automotriz','Servicios relacionados con vehículos');
