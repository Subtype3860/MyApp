-- 0: прежние показания без указания ДВС; 1 и 2: отдельные двигатели.
ALTER TABLE public.vehicle_hour_readings
    ADD COLUMN IF NOT EXISTS engine_number integer NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS public.vehicle_engine_settings (
    vehicle_id uuid NOT NULL REFERENCES public.number_car(id) ON DELETE CASCADE,
    engine_number integer NOT NULL CHECK (engine_number IN (1,2)),
    hours_at_replacement numeric CHECK (hours_at_replacement >= 0),
    PRIMARY KEY (vehicle_id,engine_number)
);
