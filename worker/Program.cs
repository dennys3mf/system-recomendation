using MySqlConnector;
using StackExchange.Redis;


var builder = WebApplication.CreateBuilder(args);

// Configurar conexión a Redis
var redisConnectionString = "localhost:6379"; // Ajusta según sea necesario
var redis = ConnectionMultiplexer.Connect(redisConnectionString);

builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// Configurar conexión a MySQL
var mysqlConnectionString = "Server=localhost;Port=3307;Database=mydatabase;User=myuser;Password=mypassword;";
builder.Services.AddTransient<MySqlConnection>(_ => new MySqlConnection(mysqlConnectionString)); // Transient para conexiones únicas

// Inicializar la base de datos y crear la tabla si no existe
using (var mysqlConnection = new MySqlConnection(mysqlConnectionString))
{
    mysqlConnection.Open();

    var createTableQuery = @"
        CREATE TABLE IF NOT EXISTS user_genres (
            user_id VARCHAR(255) PRIMARY KEY,
            genres TEXT
        )";

    using (var createTableCommand = new MySqlCommand(createTableQuery, mysqlConnection))
    {
        createTableCommand.ExecuteNonQuery(); // Crear la tabla si no existe
    }

    mysqlConnection.Close();
}

builder.Services.AddControllers();

var app = builder.Build();

app.UseRouting();

// Iniciar la suscripción a un canal de Redis
var subscriber = redis.GetSubscriber();
subscriber.Subscribe("update_channel", async (channel, message) =>
{
    // Aquí procesamos el mensaje y actualizamos MySQL
    var userId = message.ToString(); // Asumimos que el mensaje contiene la clave de usuario

    var db = redis.GetDatabase();
    var value = await db.StringGetAsync(userId);

    using (var mysqlConnection = new MySqlConnection(mysqlConnectionString))
    {
        await mysqlConnection.OpenAsync();

        var query = @"INSERT INTO user_genres (user_id, genres) 
                      VALUES (@user_id, @genres)
                      ON DUPLICATE KEY UPDATE genres = @genres";

        using (var command = new MySqlCommand(query, mysqlConnection))
        {
            command.Parameters.AddWithValue("@user_id", userId);
            command.Parameters.AddWithValue("@genres", value.ToString());

            await command.ExecuteNonQueryAsync();
        }

        await mysqlConnection.CloseAsync();
    }

    Console.WriteLine($"Usuario {userId} actualizado en MySQL con géneros '{value}'");
});

// Endpoint GET para obtener todos los usuarios y sus géneros
app.MapGet("/mysql/users", async (MySqlConnection mysqlConnection) =>
{
    List<Dictionary<string, string>> results = new(); // Usamos diccionarios para flexibilidad

    try
    {
        await mysqlConnection.OpenAsync();

        var query = "SELECT user_id, genres FROM user_genres"; // Consulta para obtener todos los usuarios y géneros

        using (var command = new MySqlCommand(query, mysqlConnection))
        {
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var record = new Dictionary<string, string>(); // Crear un nuevo diccionario para cada fila

                    // Verificar si las columnas existen antes de leerlas
                    if (reader.HasRows)
                    {
                        record["user_id"] = reader.GetString(reader.GetOrdinal("user_id")); // Lee por nombre de columna
                        record["genres"] = reader.GetString(reader.GetOrdinal("genres")); // Lee por nombre de columna
                    }

                    results.Add(record); // Agregar el diccionario a la lista
                }
            }
        }

        await mysqlConnection.CloseAsync(); // Cerrar conexión
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error al obtener datos de MySQL: {ex.Message}");
        return Results.Problem("Hubo un problema al obtener datos de la base de datos."); // Manejo de errores
    }

    return Results.Ok(results); // Devolver resultados como respuesta HTTP
});

app.MapGet("/mysql/users/{user_id}", async (string user_id, MySqlConnection mysqlConnection) =>
{
    try
    {
        await mysqlConnection.OpenAsync(); // Abrir conexión a MySQL

        var query = "SELECT user_id, genres FROM user_genres WHERE user_id = @user_id"; // Consulta para obtener usuario por ID

        using (var command = new MySqlCommand(query, mysqlConnection))
        {
            command.Parameters.AddWithValue("@user_id", user_id); // Usar parámetro para evitar SQL Injection

            using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    var result = new Dictionary<string, string>
                    {
                        { "user_id", reader.GetString(reader.GetOrdinal("user_id")) },
                        { "genres", reader.GetString(reader.GetOrdinal("genres")) }
                    };

                    await mysqlConnection.CloseAsync(); // Cerrar conexión

                    return Results.Ok(result); // Devolver resultado como JSON
                }
                else
                {
                    await mysqlConnection.CloseAsync();
                    return Results.NotFound($"No se encontró usuario con ID: {user_id}"); // Si no se encuentra el usuario
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error al obtener datos de MySQL: {ex.Message}");
        return Results.Problem("Hubo un problema al obtener datos de la base de datos."); // Manejo de errores
    }
});

app.Run();