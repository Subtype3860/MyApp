namespace MyApp.Infrastructure.Db;

public static class LocalAdviceSchema
{
    public const string Sql = """
        CREATE TABLE IF NOT EXISTS vehicle_shift_outcomes (
          vehicle_id uuid NOT NULL REFERENCES number_car(id),
          date date NOT NULL,
          shift integer NOT NULL CHECK (shift IN (1,2)),
          ready_whole_shift boolean NOT NULL,
          confirmed_by uuid NOT NULL,
          recorded_at timestamptz NOT NULL,
          PRIMARY KEY (vehicle_id, date, shift)
        );
        """;
}
