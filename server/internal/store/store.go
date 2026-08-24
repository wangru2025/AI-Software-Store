package store

import (
	"context"
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"encoding/json"
	"errors"
	"math"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/jackc/pgx/v5/pgconn"
	"golang.org/x/crypto/bcrypt"
)

type Store struct {
	db *sql.DB
}

func New(db *sql.DB) *Store {
	return &Store{db: db}
}

func (s *Store) Migrate() error {
	queries := []string{
		`create table if not exists users (
			id bigserial primary key,
			username text not null unique,
			nickname text not null,
			password_hash text not null,
			created_at timestamptz not null default now()
		)`,
		`create table if not exists sessions (
			token text primary key,
			user_id bigint not null references users(id) on delete cascade,
			created_at timestamptz not null default now()
		)`,
		`create table if not exists software (
			id text primary key,
			owner_user_id bigint not null references users(id) on delete cascade,
			name text not null,
			category text not null default '应用软件',
			summary text not null,
			created_at timestamptz not null default now(),
			deleted_at timestamptz
		)`,
		`create table if not exists software_versions (
			id bigserial primary key,
			software_id text not null references software(id) on delete cascade,
			version text not null,
			manifest_json jsonb not null,
			package_path text not null,
			sha256 text not null,
			changelog text not null,
			status text not null check (status in ('Draft', 'Published')),
			created_at timestamptz not null default now(),
			published_at timestamptz,
			download_count integer not null default 0,
			unique(software_id, version)
		)`,
		`create table if not exists ratings (
			id text primary key,
			software_id text not null references software(id) on delete cascade,
			user_id bigint not null references users(id) on delete cascade,
			stars integer not null check (stars between 1 and 5),
			comment text not null default '',
			created_at timestamptz not null default now(),
			updated_at timestamptz not null default now(),
			unique(software_id, user_id)
		)`,
		`create table if not exists rating_replies (
			id text primary key,
			rating_id text not null references ratings(id) on delete cascade,
			parent_reply_id text references rating_replies(id) on delete cascade,
			user_id bigint not null references users(id) on delete cascade,
			body text not null,
			created_at timestamptz not null default now()
		)`,
		`create table if not exists download_records (
			id bigserial primary key,
			software_id text not null,
			version text not null,
			created_at timestamptz not null default now()
		)`,
	}
	for _, query := range queries {
		if _, err := s.db.Exec(query); err != nil {
			return err
		}
	}
	_, _ = s.db.Exec(`alter table software add column if not exists deleted_at timestamptz`)
	_, _ = s.db.Exec(`alter table software add column if not exists category text not null default '应用软件'`)
	_, _ = s.db.Exec(`update software set category='应用软件' where category is null or btrim(category)=''`)
	return nil
}

func (s *Store) Register(ctx context.Context, username, nickname, password string) (string, UserSession, error) {
	hash, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	if err != nil {
		return "", UserSession{}, err
	}
	var id int64
	err = s.db.QueryRowContext(ctx, `insert into users(username,nickname,password_hash) values($1,$2,$3) returning id`, username, nickname, string(hash)).Scan(&id)
	if err != nil {
		return "", UserSession{}, err
	}
	token, err := s.createSession(ctx, id)
	return token, UserSession{Username: username, Nickname: nickname}, err
}

func (s *Store) Login(ctx context.Context, username, password string) (string, UserSession, error) {
	var user User
	err := s.db.QueryRowContext(ctx, `select id, username, nickname, password_hash from users where username=$1`, username).
		Scan(&user.ID, &user.Username, &user.Nickname, &user.PasswordHash)
	if err != nil {
		return "", UserSession{}, errors.New("用户名或密码错误")
	}
	if bcrypt.CompareHashAndPassword([]byte(user.PasswordHash), []byte(password)) != nil {
		return "", UserSession{}, errors.New("用户名或密码错误")
	}
	token, err := s.createSession(ctx, user.ID)
	return token, UserSession{Username: user.Username, Nickname: user.Nickname}, err
}

func (s *Store) UserByToken(ctx context.Context, token string) (User, error) {
	var user User
	err := s.db.QueryRowContext(ctx, `select u.id,u.username,u.nickname,u.password_hash from sessions s join users u on u.id=s.user_id where s.token=$1`, token).
		Scan(&user.ID, &user.Username, &user.Nickname, &user.PasswordHash)
	return user, err
}

func (s *Store) UpdateProfile(ctx context.Context, userID int64, username, nickname string) error {
	_, err := s.db.ExecContext(ctx, `update users set username=$1,nickname=$2 where id=$3`, username, nickname, userID)
	return err
}

func (s *Store) ChangePassword(ctx context.Context, user User, oldPassword, newPassword string) error {
	if bcrypt.CompareHashAndPassword([]byte(user.PasswordHash), []byte(oldPassword)) != nil {
		return errors.New("旧密码不正确")
	}
	hash, err := bcrypt.GenerateFromPassword([]byte(newPassword), bcrypt.DefaultCost)
	if err != nil {
		return err
	}
	_, err = s.db.ExecContext(ctx, `update users set password_hash=$1 where id=$2`, string(hash), user.ID)
	return err
}

func (s *Store) ListPublishedSoftware(ctx context.Context) ([]SoftwareItem, error) {
	rows, err := s.db.QueryContext(ctx, `
		select sw.id, sw.name, latest.version, u.username, coalesce(nullif(sw.category, ''), '应用软件'), sw.summary, coalesce(latest.published_at, latest.created_at), stats.download_count, latest.sha256, latest.status
		from software sw
		join users u on u.id=sw.owner_user_id
		join lateral (
			select * from software_versions v
			where v.software_id=sw.id and v.status='Published'
			order by coalesce(v.published_at, v.created_at) desc
			limit 1
		) latest on true
		join lateral (
			select coalesce(max(v.download_count), 0) as download_count
			from software_versions v
			where v.software_id=sw.id
		) stats on true
		where sw.deleted_at is null
		order by coalesce(latest.published_at, latest.created_at) desc`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	list := []SoftwareItem{}
	for rows.Next() {
		var item SoftwareItem
		if err := rows.Scan(&item.Id, &item.Name, &item.Version, &item.Author, &item.Category, &item.Summary, &item.PublishedAt, &item.DownloadCount, &item.PackageSha256, &item.Status); err != nil {
			return nil, err
		}
		s.fillRatingStats(ctx, &item)
		item.Changelogs, _ = s.Changelogs(ctx, item.Id)
		list = append(list, item)
	}
	return list, rows.Err()
}

func (s *Store) Software(ctx context.Context, id string) (SoftwareItem, error) {
	var item SoftwareItem
	err := s.db.QueryRowContext(ctx, `
		select sw.id, sw.name, latest.version, u.username, coalesce(nullif(sw.category, ''), '应用软件'), sw.summary, coalesce(latest.published_at, latest.created_at), stats.download_count, latest.sha256, latest.status
		from software sw
		join users u on u.id=sw.owner_user_id
		join lateral (
			select * from software_versions v where v.software_id=sw.id order by coalesce(v.published_at, v.created_at) desc limit 1
		) latest on true
		join lateral (
			select coalesce(max(v.download_count), 0) as download_count
			from software_versions v
			where v.software_id=sw.id
		) stats on true
		where sw.id=$1 and sw.deleted_at is null`, id).Scan(&item.Id, &item.Name, &item.Version, &item.Author, &item.Category, &item.Summary, &item.PublishedAt, &item.DownloadCount, &item.PackageSha256, &item.Status)
	if err != nil {
		return item, err
	}
	s.fillRatingStats(ctx, &item)
	item.Changelogs, _ = s.Changelogs(ctx, item.Id)
	return item, nil
}

func (s *Store) Changelogs(ctx context.Context, softwareID string) ([]ChangelogEntry, error) {
	rows, err := s.db.QueryContext(ctx, `select version, coalesce(published_at, created_at), changelog from software_versions where software_id=$1 order by coalesce(published_at, created_at) desc`, softwareID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	list := []ChangelogEntry{}
	for rows.Next() {
		var entry ChangelogEntry
		if err := rows.Scan(&entry.Version, &entry.Date, &entry.Body); err != nil {
			return nil, err
		}
		list = append(list, entry)
	}
	return list, rows.Err()
}

func (s *Store) MySubmissions(ctx context.Context, userID int64) ([]SubmissionItem, error) {
	rows, err := s.db.QueryContext(ctx, `
		select sw.id, sw.name, v.version, coalesce(nullif(sw.category, ''), '应用软件'), sw.summary, coalesce(v.published_at, v.created_at), stats.download_count, v.status
		from software sw
		join lateral (
			select * from software_versions v
			where v.software_id=sw.id
			order by coalesce(v.published_at, v.created_at) desc, v.id desc
			limit 1
		) v on true
		join lateral (
			select coalesce(max(v.download_count), 0) as download_count
			from software_versions v
			where v.software_id=sw.id
		) stats on true
		where sw.owner_user_id=$1 and sw.deleted_at is null
		order by coalesce(v.published_at, v.created_at) desc`, userID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	list := []SubmissionItem{}
	for rows.Next() {
		var item SubmissionItem
		if err := rows.Scan(&item.SoftwareId, &item.Name, &item.Version, &item.Category, &item.Summary, &item.PublishedAt, &item.DownloadCount, &item.Status); err != nil {
			return nil, err
		}
		var avg sql.NullFloat64
		_ = s.db.QueryRowContext(ctx, `select avg(stars), count(*) from ratings where software_id=$1`, item.SoftwareId).Scan(&avg, &item.RatingCount)
		if avg.Valid {
			item.AverageRating = math.Round(avg.Float64*10) / 10
		}
		list = append(list, item)
	}
	return list, rows.Err()
}

func (s *Store) SaveSubmission(ctx context.Context, userID int64, manifest Manifest, packagePath, sha256, changelog string) error {
	manifestJSON, _ := json.Marshal(manifest)
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()

	var ownerID int64
	err = tx.QueryRowContext(ctx, `select owner_user_id from software where id=$1`, manifest.ID).Scan(&ownerID)
	if err != nil && err != sql.ErrNoRows {
		return err
	}
	if err == nil && ownerID != userID {
		return errors.New("这个软件 id 已被其它投稿者使用")
	}
	existingSoftware := err == nil
	carriedDownloadCount := 0

	var nameOwnerID int64
	err = tx.QueryRowContext(ctx, `select owner_user_id from software where lower(name)=lower($1) and deleted_at is null limit 1`, manifest.Name).Scan(&nameOwnerID)
	if err != nil && err != sql.ErrNoRows {
		return err
	}
	if err == nil && nameOwnerID != userID {
		return errors.New("这个软件名称已被其它投稿者使用")
	}

	if existingSoftware {
		var latestVersion string
		err = tx.QueryRowContext(ctx, `
			select version,
			       coalesce((select max(download_count) from software_versions where software_id=$1), 0)
			from software_versions
			where software_id=$1
			order by coalesce(published_at, created_at) desc, id desc
			limit 1`, manifest.ID).Scan(&latestVersion, &carriedDownloadCount)
		if err != nil {
			return err
		}
		if compareVersion(manifest.Version, latestVersion) <= 0 {
			return errors.New("新版本号必须高于当前版本。")
		}
	}

	_, err = tx.ExecContext(ctx, `
		insert into software(id, owner_user_id, name, category, summary)
		values($1,$2,$3,$4,$5)
		on conflict(id) do update set name=excluded.name, category=excluded.category, summary=excluded.summary, deleted_at=null`,
		manifest.ID, userID, manifest.Name, categoryOrDefault(manifest.Category), manifest.Summary)
	if err != nil {
		return err
	}
	_, err = tx.ExecContext(ctx, `
		insert into software_versions(software_id, version, manifest_json, package_path, sha256, changelog, status, published_at, download_count)
		values($1,$2,$3,$4,$5,$6,$7,$8,$9)`,
		manifest.ID, manifest.Version, string(manifestJSON), packagePath, sha256, changelog, submissionStatus(existingSoftware), submissionPublishedAt(existingSoftware), carriedDownloadCount)
	if err != nil {
		var pgErr *pgconn.PgError
		if errors.As(err, &pgErr) && pgErr.Code == "23505" && pgErr.ConstraintName == "software_versions_software_id_version_key" {
			return errors.New("这个软件的当前版本已经投稿过，请修改版本号后再上传。")
		}
		return err
	}
	stalePackagePaths, err := stalePackageFiles(ctx, tx, manifest.ID, 3)
	if err != nil {
		return err
	}

	if err := pruneSoftwareVersions(ctx, tx, manifest.ID, stalePackagePaths); err != nil {
		return err
	}

	if err := tx.Commit(); err != nil {
		return err
	}

	removePackageFiles(stalePackagePaths)
	return nil
}

func stalePackageFiles(ctx context.Context, tx *sql.Tx, softwareID string, keep int) ([]string, error) {
	rows, err := tx.QueryContext(ctx, `
		select package_path
		from software_versions
		where software_id=$1
		order by coalesce(published_at, created_at) desc, id desc
		offset $2`, softwareID, keep)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var paths []string
	for rows.Next() {
		var packagePath string
		if err := rows.Scan(&packagePath); err != nil {
			return nil, err
		}
		if packagePath != "" {
			paths = append(paths, packagePath)
		}
	}
	return paths, rows.Err()
}

func pruneSoftwareVersions(ctx context.Context, tx *sql.Tx, softwareID string, packagePaths []string) error {
	for _, packagePath := range packagePaths {
		if _, err := tx.ExecContext(ctx, `delete from software_versions where software_id=$1 and package_path=$2`, softwareID, packagePath); err != nil {
			return err
		}
	}
	return nil
}

func removePackageFiles(packagePaths []string) {
	for _, packagePath := range packagePaths {
		if packagePath == "" {
			continue
		}
		if err := os.Remove(packagePath); err != nil && !errors.Is(err, os.ErrNotExist) {
			continue
		}
		removeEmptyParents(filepath.Dir(packagePath), 2)
	}
}

func removeEmptyParents(dir string, depth int) {
	for i := 0; i < depth && dir != "." && dir != string(filepath.Separator); i++ {
		if err := os.Remove(dir); err != nil {
			return
		}
		dir = filepath.Dir(dir)
	}
}

func submissionStatus(existingSoftware bool) string {
	if existingSoftware {
		return "Published"
	}
	return "Draft"
}

func submissionPublishedAt(existingSoftware bool) any {
	if existingSoftware {
		return time.Now()
	}
	return nil
}

func categoryOrDefault(category string) string {
	category = strings.TrimSpace(category)
	if category == "" {
		return "应用软件"
	}
	return category
}

func compareVersion(left, right string) int {
	leftParts := strings.Split(left, ".")
	rightParts := strings.Split(right, ".")
	max := len(leftParts)
	if len(rightParts) > max {
		max = len(rightParts)
	}
	for i := 0; i < max; i++ {
		var l, r int
		if i < len(leftParts) {
			l = atoiVersionPart(leftParts[i])
		}
		if i < len(rightParts) {
			r = atoiVersionPart(rightParts[i])
		}
		if l < r {
			return -1
		}
		if l > r {
			return 1
		}
	}
	return 0
}

func atoiVersionPart(value string) int {
	n := 0
	for _, r := range value {
		if r < '0' || r > '9' {
			break
		}
		n = n*10 + int(r-'0')
	}
	return n
}

func (s *Store) ToggleSubmissionStatus(ctx context.Context, userID int64, softwareID string) error {
	_, err := s.db.ExecContext(ctx, `
		update software_versions v
		set status = case when status='Draft' then 'Published' else 'Draft' end,
		    published_at = case when status='Draft' then now() else published_at end
		from software sw
		where sw.id=v.software_id and sw.owner_user_id=$1 and sw.id=$2 and sw.deleted_at is null`, userID, softwareID)
	return err
}

func (s *Store) UpdateSoftwareInfo(ctx context.Context, userID int64, softwareID, name, summary, category string) error {
	_, err := s.db.ExecContext(ctx, `update software set name=$1, summary=$2, category=$3 where id=$4 and owner_user_id=$5 and deleted_at is null`, name, summary, categoryOrDefault(category), softwareID, userID)
	return err
}

func (s *Store) DeleteSubmission(ctx context.Context, userID int64, softwareID string) error {
	_, err := s.db.ExecContext(ctx, `update software set deleted_at=now() where id=$1 and owner_user_id=$2 and deleted_at is null`, softwareID, userID)
	return err
}

func (s *Store) PackageForDownload(ctx context.Context, softwareID, version string) (string, error) {
	var path string
	err := s.db.QueryRowContext(ctx, `
		select v.package_path
		from software_versions v
		join software sw on sw.id=v.software_id
		where v.software_id=$1 and v.version=$2 and v.status='Published' and sw.deleted_at is null`, softwareID, version).Scan(&path)
	if err != nil {
		return "", err
	}
	_, _ = s.db.ExecContext(ctx, `
		update software_versions target
		set download_count = greatest(
			target.download_count,
			coalesce((select max(v.download_count) from software_versions v where v.software_id=$1), 0)
		) + 1
		where target.software_id=$1 and target.version=$2`, softwareID, version)
	_, _ = s.db.ExecContext(ctx, `insert into download_records(software_id, version) values($1,$2)`, softwareID, version)
	return path, nil
}

func (s *Store) Ratings(ctx context.Context, softwareID string) ([]RatingItem, error) {
	rows, err := s.db.QueryContext(ctx, `
		select r.id,r.software_id,u.username,u.nickname,r.stars,r.comment,r.created_at,
		       (select count(*) from rating_replies rr where rr.rating_id=r.id)
		from ratings r join users u on u.id=r.user_id
		where r.software_id=$1 order by r.created_at desc`, softwareID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	list := []RatingItem{}
	for rows.Next() {
		var item RatingItem
		if err := rows.Scan(&item.Id, &item.SoftwareId, &item.Username, &item.Nickname, &item.Stars, &item.Comment, &item.CreatedAt, &item.ReplyCount); err != nil {
			return nil, err
		}
		list = append(list, item)
	}
	return list, rows.Err()
}

func (s *Store) SaveRating(ctx context.Context, userID int64, softwareID string, stars int, comment string) error {
	if s.IsSoftwareOwner(ctx, userID, softwareID) {
		return errors.New("开发者不能给自己的软件评分。")
	}

	id := randomID()
	_, err := s.db.ExecContext(ctx, `
		insert into ratings(id,software_id,user_id,stars,comment)
		values($1,$2,$3,$4,$5)`,
		id, softwareID, userID, stars, comment)
	if err != nil {
		var pgErr *pgconn.PgError
		if errors.As(err, &pgErr) && pgErr.Code == "23505" && pgErr.ConstraintName == "ratings_software_id_user_id_key" {
			return errors.New("你已经给这个软件评过分。")
		}
	}
	return err
}

func (s *Store) Replies(ctx context.Context, ratingID string) ([]RatingReply, error) {
	rows, err := s.db.QueryContext(ctx, `
		select rr.id,rr.rating_id,coalesce(rr.parent_reply_id,''),u.nickname,rr.body,rr.created_at
		from rating_replies rr join users u on u.id=rr.user_id
		where rr.rating_id=$1 order by rr.created_at asc`, ratingID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	list := []RatingReply{}
	for rows.Next() {
		var item RatingReply
		if err := rows.Scan(&item.Id, &item.RatingId, &item.ParentReplyId, &item.Nickname, &item.Body, &item.CreatedAt); err != nil {
			return nil, err
		}
		list = append(list, item)
	}
	return list, rows.Err()
}

func (s *Store) AddReply(ctx context.Context, userID int64, ratingID, parentReplyID, body string) error {
	var parent any
	if parentReplyID != "" {
		parent = parentReplyID
	}
	_, err := s.db.ExecContext(ctx, `insert into rating_replies(id,rating_id,parent_reply_id,user_id,body) values($1,$2,$3,$4,$5)`, randomID(), ratingID, parent, userID, body)
	return err
}

func (s *Store) IsSoftwareOwner(ctx context.Context, userID int64, softwareID string) bool {
	var ok bool
	_ = s.db.QueryRowContext(ctx, `select exists(select 1 from software where id=$1 and owner_user_id=$2)`, softwareID, userID).Scan(&ok)
	return ok
}

func (s *Store) RatingSoftwareID(ctx context.Context, ratingID string) (string, error) {
	var id string
	err := s.db.QueryRowContext(ctx, `select software_id from ratings where id=$1`, ratingID).Scan(&id)
	return id, err
}

func (s *Store) fillRatingStats(ctx context.Context, item *SoftwareItem) {
	var avg sql.NullFloat64
	_ = s.db.QueryRowContext(ctx, `select avg(stars), count(*) from ratings where software_id=$1`, item.Id).Scan(&avg, &item.RatingCount)
	if avg.Valid {
		item.AverageRating = math.Round(avg.Float64*10) / 10
	}
}

func (s *Store) createSession(ctx context.Context, userID int64) (string, error) {
	token := randomID() + randomID()
	_, err := s.db.ExecContext(ctx, `insert into sessions(token,user_id) values($1,$2)`, token, userID)
	return token, err
}

func randomID() string {
	var b [16]byte
	_, _ = rand.Read(b[:])
	return hex.EncodeToString(b[:])
}
