package main

import (
	"database/sql"
	"log"
	"net/http"
	"os"
	"time"

	_ "github.com/jackc/pgx/v5/stdlib"

	"aishop-server/internal/api"
	"aishop-server/internal/store"
)

func main() {
	addr := getenv("AISHOP_ADDR", ":8080")
	dsn := getenv("AISHOP_DATABASE_URL", "postgres://postgres:postgres@localhost:5432/aishop?sslmode=disable")
	dataDir := getenv("AISHOP_DATA_DIR", "./data")
	accelPrefix := getenv("AISHOP_ACCEL_PREFIX", "/internal-packages/")

	db, err := sql.Open("pgx", dsn)
	if err != nil {
		log.Fatal(err)
	}
	db.SetMaxOpenConns(10)
	db.SetMaxIdleConns(5)
	db.SetConnMaxLifetime(30 * time.Minute)

	repo := store.New(db)
	if err := repo.Migrate(); err != nil {
		log.Fatal(err)
	}

	server := api.NewServer(repo, api.Config{
		DataDir:          dataDir,
		AccelPrefix:      accelPrefix,
		ClientVersion:    getenv("AISHOP_CLIENT_VERSION", "1.0.0"),
		ClientUpdateURL:  getenv("AISHOP_CLIENT_UPDATE_URL", ""),
		ClientUpdateHash: getenv("AISHOP_CLIENT_UPDATE_SHA256", ""),
		ClientChangelog:  getenv("AISHOP_CLIENT_CHANGELOG", ""),
	})

	log.Printf("AI 软件商店服务端监听 %s", addr)
	log.Fatal(http.ListenAndServe(addr, server.Routes()))
}

func getenv(key, fallback string) string {
	value := os.Getenv(key)
	if value == "" {
		return fallback
	}
	return value
}
