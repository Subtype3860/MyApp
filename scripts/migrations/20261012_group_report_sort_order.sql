-- Чем меньше значение, тем выше группа в Выходе ГТО.
-- При одинаковых значениях применяется алфавитный порядок.
ALTER TABLE public.group_car
    ADD COLUMN IF NOT EXISTS report_sort_order integer NOT NULL DEFAULT 0;
