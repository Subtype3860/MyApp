-- Однократная привязка существующих типов к статическим SVG.
-- При повторном запуске сохраняются изменения icon_path, включая NULL.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'type_car' AND column_name = 'icon_path'
    ) THEN
        ALTER TABLE public.type_car ADD COLUMN icon_path varchar(255);
        UPDATE public.type_car
        SET icon_path = CASE LOWER(BTRIM(full_name))
            WHEN 'экскаватор' THEN '/icons/vehicles/excavator.svg'
            WHEN 'экскаваторы' THEN '/icons/vehicles/excavator.svg'
            WHEN 'карьерный самосвал' THEN '/icons/vehicles/dump-truck.svg'
            WHEN 'самосвал' THEN '/icons/vehicles/dump-truck.svg'
            WHEN 'самосвалы' THEN '/icons/vehicles/dump-truck.svg'
            WHEN 'автосамосвал' THEN '/icons/vehicles/dump-truck.svg'
            WHEN 'автосамосвалы' THEN '/icons/vehicles/dump-truck.svg'
            WHEN 'бульдозер' THEN '/icons/vehicles/bulldozer.svg'
            WHEN 'бульдозеры' THEN '/icons/vehicles/bulldozer.svg'
            WHEN 'грейдер' THEN '/icons/vehicles/grader.svg'
            WHEN 'грейдеры' THEN '/icons/vehicles/grader.svg'
            WHEN 'автогрейдер' THEN '/icons/vehicles/grader.svg'
            WHEN 'автогрейдеры' THEN '/icons/vehicles/grader.svg'
            WHEN 'вахтовый автобус' THEN '/icons/vehicles/shift-bus.svg'
            WHEN 'вахтовые автобусы' THEN '/icons/vehicles/shift-bus.svg'
            WHEN 'легковой автомобиль' THEN '/icons/vehicles/passenger-car.svg'
            WHEN 'легковые автомобили' THEN '/icons/vehicles/passenger-car.svg'
            WHEN 'погрузчик' THEN '/icons/vehicles/loader.svg'
            WHEN 'погрузчики' THEN '/icons/vehicles/loader.svg'
            WHEN 'фронтальный погрузчик' THEN '/icons/vehicles/loader.svg'
            WHEN 'фронтальные погрузчики' THEN '/icons/vehicles/loader.svg'
            WHEN 'гр.автомобиль' THEN '/icons/vehicles/cargo-truck.svg'
            WHEN 'грузовой автомобиль' THEN '/icons/vehicles/cargo-truck.svg'
            WHEN 'грузовые автомобили' THEN '/icons/vehicles/cargo-truck.svg'
            ELSE NULL
        END;
    END IF;
END $$;
