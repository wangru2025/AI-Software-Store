package store

import "time"

type User struct {
	ID           int64
	Username     string
	Nickname     string
	PasswordHash string
}

type UserSession struct {
	Username string `json:"Username"`
	Nickname string `json:"Nickname"`
}

type SoftwareItem struct {
	Id            string           `json:"Id"`
	Name          string           `json:"Name"`
	Version       string           `json:"Version"`
	Author        string           `json:"Author"`
	Summary       string           `json:"Summary"`
	PublishedAt   time.Time        `json:"PublishedAt"`
	DownloadCount int              `json:"DownloadCount"`
	PackageSha256 string           `json:"PackageSha256"`
	AverageRating float64          `json:"AverageRating"`
	RatingCount   int              `json:"RatingCount"`
	Status        string           `json:"Status"`
	Changelogs    []ChangelogEntry `json:"Changelogs"`
}

type SubmissionItem struct {
	SoftwareId    string    `json:"SoftwareId"`
	Name          string    `json:"Name"`
	Version       string    `json:"Version"`
	Summary       string    `json:"Summary"`
	PublishedAt   time.Time `json:"PublishedAt"`
	DownloadCount int       `json:"DownloadCount"`
	AverageRating float64   `json:"AverageRating"`
	RatingCount   int       `json:"RatingCount"`
	Status        string    `json:"Status"`
}

type ChangelogEntry struct {
	Version string    `json:"Version"`
	Date    time.Time `json:"Date"`
	Body    string    `json:"Body"`
}

type RatingItem struct {
	Id         string    `json:"Id"`
	SoftwareId string    `json:"SoftwareId"`
	Nickname   string    `json:"Nickname"`
	Stars      int       `json:"Stars"`
	Comment    string    `json:"Comment"`
	CreatedAt  time.Time `json:"CreatedAt"`
	ReplyCount int       `json:"ReplyCount"`
}

type RatingReply struct {
	Id            string    `json:"Id"`
	RatingId      string    `json:"RatingId"`
	ParentReplyId string    `json:"ParentReplyId"`
	Nickname      string    `json:"Nickname"`
	Body          string    `json:"Body"`
	CreatedAt     time.Time `json:"CreatedAt"`
}

type Manifest struct {
	ID            string `json:"id"`
	Name          string `json:"name"`
	Version       string `json:"version"`
	Author        string `json:"author"`
	Summary       string `json:"summary"`
	RequiresAdmin bool   `json:"requiresAdmin"`
	Install       string `json:"install"`
	Uninstall     string `json:"uninstall"`
	Update        string `json:"update"`
	UpdateMode    string `json:"updateMode"`
}
