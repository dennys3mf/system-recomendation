from flask import Flask, render_template, request, redirect, url_for
import redis
import requests
app = Flask(__name__)

# Conexión a Redis
r = redis.Redis(host='localhost', port=6379, db=0)  # Conexión al servidor Redis local


all_genres = [
    "Action", "Adventure", "Animation", "Children", "Comedy", "Crime", 
    "Documentary", "Drama", "Fantasy", "Film-Noir", "Horror", "Musical", 
    "Mystery", "Romance", "Sci-Fi", "Thriller", "War", "Western"
]

@app.route('/')
def index():
    return render_template('index.html')  # Renderiza la plantilla HTML principal


# Rutas para las opciones de usuario
@app.route('/usuario/<user_id>', methods=['GET', 'POST'])

def select_genres(user_id):
    if request.method == 'POST':

        selected_genres = request.form.getlist('genres') 
         # Obtiene los géneros seleccionados

        r.set(f"user:{user_id}", ','.join(selected_genres))  # Almacenar en Redis
        r.publish("update_channel", f"user:{user_id}")

        return redirect(url_for('recommendations', user_id=f"user:{user_id}"))



    # Renderiza la página para seleccionar géneros
    return render_template('generos.html', user_id=user_id, genres=all_genres)

@app.route('/recommendations/<user_id>', methods=['GET'])
def recommendations(user_id):
    # Obtener las recomendaciones de películas desde el endpoint de recomendaciones
    response = requests.get(f"http://localhost:5050/recommendations/{user_id}")

    if response.status_code != 200:
        return f"Error al obtener recomendaciones para el usuario {user_id}"

    # Obtener las recomendaciones del JSON
    recommended_movies = response.json().get("recommended_movies", [])

    return render_template('recomendacion.html', 
                           user_id=user_id, 
                           recommended_movies=recommended_movies)




if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000,debug=True)  # Ejecutar la aplicación en modo depuración
