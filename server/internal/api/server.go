package api

import (
	"archive/zip"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"os"
	"path"
	"path/filepath"
	"regexp"
	"strings"
	"time"

	"aishop-server/internal/store"
)

type Config struct {
	DataDir          string
	AccelPrefix      string
	ClientVersion    string
	ClientUpdateURL  string
	ClientUpdateHash string
	ClientChangelog  string
}

type Server struct {
	store *store.Store
	cfg   Config
}

func NewServer(repo *store.Store, cfg Config) *Server {
	return &Server{store: repo, cfg: cfg}
}

func (s *Server) Routes() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("/api/auth/register", s.handleRegister)
	mux.HandleFunc("/api/auth/login", s.handleLogin)
	mux.HandleFunc("/api/me/profile", s.withAuth(s.handleProfile))
	mux.HandleFunc("/api/me/password", s.withAuth(s.handlePassword))
	mux.HandleFunc("/api/me/submissions", s.withAuth(s.handleMySubmissions))
	mux.HandleFunc("/api/me/submissions/", s.withAuth(s.handleSubmissionAction))
	mux.HandleFunc("/api/submissions", s.withAuth(s.handleUploadSubmission))
	mux.HandleFunc("/api/software", s.handleSoftwareList)
	mux.HandleFunc("/api/software/", s.handleSoftwarePath)
	mux.HandleFunc("/api/ratings/", s.handleRatingPath)
	mux.HandleFunc("/api/client/update", s.handleClientUpdate)
	return cors(mux)
}

func (s *Server) handleRegister(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		methodNotAllowed(w)
		return
	}
	var req struct {
		Username string `json:"username"`
		Nickname string `json:"nickname"`
		Password string `json:"password"`
	}
	if !decode(w, r, &req) || !validateAccount(w, req.Username, req.Nickname, req.Password) {
		return
	}
	token, user, err := s.store.Register(r.Context(), strings.TrimSpace(req.Username), strings.TrimSpace(req.Nickname), req.Password)
	if err != nil {
		fail(w, http.StatusBadRequest, err.Error())
		return
	}
	writeJSON(w, map[string]any{"Token": token, "User": user})
}

func (s *Server) handleLogin(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		methodNotAllowed(w)
		return
	}
	var req struct {
		Username string `json:"username"`
		Password string `json:"password"`
	}
	if !decode(w, r, &req) {
		return
	}
	token, user, err := s.store.Login(r.Context(), strings.TrimSpace(req.Username), req.Password)
	if err != nil {
		fail(w, http.StatusUnauthorized, err.Error())
		return
	}
	writeJSON(w, map[string]any{"Token": token, "User": user})
}

func (s *Server) handleProfile(w http.ResponseWriter, r *http.Request, user store.User) {
	if r.Method != http.MethodPost {
		methodNotAllowed(w)
		return
	}
	var req struct {
		Username string `json:"username"`
		Nickname string `json:"nickname"`
	}
	if !decode(w, r, &req) {
		return
	}
	if strings.TrimSpace(req.Username) == "" || len([]rune(req.Username)) > 20 {
		fail(w, http.StatusBadRequest, "用户名最多 20 个字符。")
		return
	}
	if strings.TrimSpace(req.Nickname) == "" || len([]rune(req.Nickname)) > 10 {
		fail(w, http.StatusBadRequest, "昵称最多 10 个字符。")
		return
	}
	if err := s.store.UpdateProfile(r.Context(), user.ID, strings.TrimSpace(req.Username), strings.TrimSpace(req.Nickname)); err != nil {
		fail(w, http.StatusBadRequest, err.Error())
		return
	}
	writeJSON(w, map[string]bool{"Ok": true})
}

func (s *Server) handlePassword(w http.ResponseWriter, r *http.Request, user store.User) {
	if r.Method != http.MethodPost {
		methodNotAllowed(w)
		return
	}
	var req struct {
		OldPassword      string `json:"oldPassword"`
		NewPassword      string `json:"newPassword"`
		RepeatedPassword string `json:"repeatedPassword"`
	}
	if !decode(w, r, &req) {
		return
	}
	if req.NewPassword != req.RepeatedPassword {
		fail(w, http.StatusBadRequest, "两次输入的新密码不一致。")
		return
	}
	if len(req.NewPassword) < 6 || len(req.NewPassword) > 20 {
		fail(w, http.StatusBadRequest, "密码必须为 6 到 20 个字符。")
		return
	}
	if err := s.store.ChangePassword(r.Context(), user, req.OldPassword, req.NewPassword); err != nil {
		fail(w, http.StatusBadRequest, err.Error())
		return
	}
	writeJSON(w, map[string]bool{"Ok": true})
}

func (s *Server) handleSoftwareList(w http.ResponseWriter, r *http.Request) {
	if r.URL.Path != "/api/software" {
		http.NotFound(w, r)
		return
	}
	if r.Method != http.MethodGet {
		methodNotAllowed(w)
		return
	}
	items, err := s.store.ListPublishedSoftware(r.Context())
	if err != nil {
		fail(w, http.StatusInternalServerError, err.Error())
		return
	}
	writeJSON(w, items)
}

func (s *Server) handleSoftwarePath(w http.ResponseWriter, r *http.Request) {
	parts := splitPath(strings.TrimPrefix(r.URL.Path, "/api/software/"))
	if len(parts) == 1 && r.Method == http.MethodGet {
		item, err := s.store.Software(r.Context(), parts[0])
		if err != nil {
			fail(w, http.StatusNotFound, "软件不存在。")
			return
		}
		writeJSON(w, item)
		return
	}
	if len(parts) == 2 && parts[1] == "ratings" {
		if r.Method == http.MethodGet {
			items, err := s.store.Ratings(r.Context(), parts[0])
			if err != nil {
				fail(w, http.StatusInternalServerError, err.Error())
				return
			}
			writeJSON(w, items)
			return
		}
		if r.Method == http.MethodPost {
			user, ok := s.authUser(w, r)
			if !ok {
				return
			}
			var req struct {
				Stars   int    `json:"stars"`
				Comment string `json:"comment"`
			}
			if !decode(w, r, &req) {
				return
			}
			if req.Stars < 1 || req.Stars > 5 {
				fail(w, http.StatusBadRequest, "星级必须在 1 到 5 之间。")
				return
			}
			if len([]rune(req.Comment)) > 1000 {
				fail(w, http.StatusBadRequest, "评论最多 1000 个字符。")
				return
			}
			if err := s.store.SaveRating(r.Context(), user.ID, parts[0], req.Stars, req.Comment); err != nil {
				fail(w, http.StatusBadRequest, err.Error())
				return
			}
			writeJSON(w, map[string]bool{"Ok": true})
			return
		}
	}
	if len(parts) == 4 && parts[1] == "versions" && parts[3] == "download" && r.Method == http.MethodGet {
		s.handleDownload(w, r, parts[0], parts[2])
		return
	}
	http.NotFound(w, r)
}

func (s *Server) handleRatingPath(w http.ResponseWriter, r *http.Request) {
	parts := splitPath(strings.TrimPrefix(r.URL.Path, "/api/ratings/"))
	if len(parts) == 2 && parts[1] == "replies" {
		if r.Method == http.MethodGet {
			items, err := s.store.Replies(r.Context(), parts[0])
			if err != nil {
				fail(w, http.StatusInternalServerError, err.Error())
				return
			}
			writeJSON(w, items)
			return
		}
		if r.Method == http.MethodPost {
			user, ok := s.authUser(w, r)
			if !ok {
				return
			}
			softwareID, err := s.store.RatingSoftwareID(r.Context(), parts[0])
			if err != nil {
				fail(w, http.StatusNotFound, "评分不存在。")
				return
			}
			if !s.store.IsSoftwareOwner(r.Context(), user.ID, softwareID) {
				fail(w, http.StatusForbidden, "只有开发者可以回复评分。")
				return
			}
			var req struct {
				ParentReplyId string `json:"parentReplyId"`
				Body          string `json:"body"`
			}
			if !decode(w, r, &req) {
				return
			}
			if strings.TrimSpace(req.Body) == "" || len([]rune(req.Body)) > 500 {
				fail(w, http.StatusBadRequest, "回复不能为空，且最多 500 个字符。")
				return
			}
			if err := s.store.AddReply(r.Context(), user.ID, parts[0], req.ParentReplyId, req.Body); err != nil {
				fail(w, http.StatusBadRequest, err.Error())
				return
			}
			writeJSON(w, map[string]bool{"Ok": true})
			return
		}
	}
	http.NotFound(w, r)
}

func (s *Server) handleMySubmissions(w http.ResponseWriter, r *http.Request, user store.User) {
	if r.Method != http.MethodGet {
		methodNotAllowed(w)
		return
	}
	items, err := s.store.MySubmissions(r.Context(), user.ID)
	if err != nil {
		fail(w, http.StatusInternalServerError, err.Error())
		return
	}
	writeJSON(w, items)
}

func (s *Server) handleSubmissionAction(w http.ResponseWriter, r *http.Request, user store.User) {
	if r.Method != http.MethodPost {
		methodNotAllowed(w)
		return
	}
	parts := splitPath(strings.TrimPrefix(r.URL.Path, "/api/me/submissions/"))
	if len(parts) == 2 && parts[1] == "toggle-status" {
		if err := s.store.ToggleSubmissionStatus(r.Context(), user.ID, parts[0]); err != nil {
			fail(w, http.StatusBadRequest, err.Error())
			return
		}
		writeJSON(w, map[string]bool{"Ok": true})
		return
	}
	if len(parts) == 2 && parts[1] == "delete" {
		if err := s.store.DeleteSubmission(r.Context(), user.ID, parts[0]); err != nil {
			fail(w, http.StatusBadRequest, err.Error())
			return
		}
		writeJSON(w, map[string]bool{"Ok": true})
		return
	}
	if len(parts) == 1 {
		var req struct {
			Name    string `json:"name"`
			Summary string `json:"summary"`
		}
		if !decode(w, r, &req) {
			return
		}
		if strings.TrimSpace(req.Name) == "" || strings.TrimSpace(req.Summary) == "" {
			fail(w, http.StatusBadRequest, "软件名称和简介不能为空。")
			return
		}
		if err := s.store.UpdateSoftwareInfo(r.Context(), user.ID, parts[0], req.Name, req.Summary); err != nil {
			fail(w, http.StatusBadRequest, err.Error())
			return
		}
		writeJSON(w, map[string]bool{"Ok": true})
		return
	}
	http.NotFound(w, r)
}

func (s *Server) handleUploadSubmission(w http.ResponseWriter, r *http.Request, user store.User) {
	if r.Method != http.MethodPost {
		methodNotAllowed(w)
		return
	}
	if err := r.ParseMultipartForm(200 << 20); err != nil {
		fail(w, http.StatusBadRequest, "投稿包过大或格式不正确。")
		return
	}
	file, header, err := r.FormFile("package")
	if err != nil {
		fail(w, http.StatusBadRequest, "请选择 zip 投稿包。")
		return
	}
	defer file.Close()
	if !strings.EqualFold(filepath.Ext(header.Filename), ".zip") {
		fail(w, http.StatusBadRequest, "投稿包必须是 zip 文件。")
		return
	}
	temp, err := os.CreateTemp("", "aishop-*.zip")
	if err != nil {
		fail(w, http.StatusInternalServerError, err.Error())
		return
	}
	tempPath := temp.Name()
	defer os.Remove(tempPath)
	hash := sha256.New()
	size, err := io.Copy(io.MultiWriter(temp, hash), file)
	temp.Close()
	if err != nil {
		fail(w, http.StatusBadRequest, "保存投稿包失败。")
		return
	}
	if size > 200<<20 {
		fail(w, http.StatusBadRequest, "投稿包不能超过 200MB。")
		return
	}
	sha := hex.EncodeToString(hash.Sum(nil))
	manifest, changelog, err := validatePackage(tempPath)
	if err != nil {
		fail(w, http.StatusBadRequest, err.Error())
		return
	}
	targetDir := filepath.Join(s.cfg.DataDir, "packages", safeName(manifest.ID), safeName(manifest.Version))
	if err := os.MkdirAll(targetDir, 0755); err != nil {
		fail(w, http.StatusInternalServerError, err.Error())
		return
	}
	targetPath := filepath.Join(targetDir, "package.zip")
	if err := copyFile(tempPath, targetPath); err != nil {
		fail(w, http.StatusInternalServerError, err.Error())
		return
	}
	if err := s.store.SaveSubmission(r.Context(), user.ID, manifest, targetPath, sha, changelog); err != nil {
		fail(w, http.StatusBadRequest, err.Error())
		return
	}
	writeJSON(w, map[string]bool{"Ok": true})
}

func (s *Server) handleDownload(w http.ResponseWriter, r *http.Request, softwareID, version string) {
	packagePath, err := s.store.PackageForDownload(r.Context(), softwareID, version)
	if err != nil {
		fail(w, http.StatusNotFound, "软件包不存在或未上架。")
		return
	}
	internalPath := path.Join(s.cfg.AccelPrefix, safeName(softwareID), safeName(version), "package.zip")
	if !strings.HasPrefix(internalPath, "/") {
		internalPath = "/" + internalPath
	}
	w.Header().Set("Content-Type", "application/zip")
	w.Header().Set("X-Accel-Redirect", internalPath)
	if os.Getenv("AISHOP_DEV_SERVE_FILES") == "1" {
		http.ServeFile(w, r, packagePath)
	}
}

func (s *Server) handleClientUpdate(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		methodNotAllowed(w)
		return
	}
	hasUpdate := s.cfg.ClientUpdateURL != ""
	writeJSON(w, map[string]any{
		"HasUpdate":   hasUpdate,
		"Version":     s.cfg.ClientVersion,
		"Changelog":   s.cfg.ClientChangelog,
		"DownloadUrl": s.cfg.ClientUpdateURL,
		"Sha256":      s.cfg.ClientUpdateHash,
	})
}

func (s *Server) withAuth(next func(http.ResponseWriter, *http.Request, store.User)) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		user, ok := s.authUser(w, r)
		if !ok {
			return
		}
		next(w, r, user)
	}
}

func (s *Server) authUser(w http.ResponseWriter, r *http.Request) (store.User, bool) {
	header := r.Header.Get("Authorization")
	if !strings.HasPrefix(header, "Bearer ") {
		fail(w, http.StatusUnauthorized, "请先登录。")
		return store.User{}, false
	}
	user, err := s.store.UserByToken(r.Context(), strings.TrimPrefix(header, "Bearer "))
	if err != nil {
		fail(w, http.StatusUnauthorized, "登录已失效，请重新登录。")
		return store.User{}, false
	}
	return user, true
}

func validatePackage(zipPath string) (store.Manifest, string, error) {
	reader, err := zip.OpenReader(zipPath)
	if err != nil {
		return store.Manifest{}, "", errors.New("zip 文件无法解压。")
	}
	defer reader.Close()
	if len(reader.File) == 0 || len(reader.File) > 2000 {
		return store.Manifest{}, "", errors.New("投稿包文件数量不符合要求。")
	}
	files := map[string]*zip.File{}
	for _, file := range reader.File {
		name := filepath.ToSlash(file.Name)
		if strings.HasPrefix(name, "/") || strings.Contains(name, "../") || strings.Contains(name, `..\`) {
			return store.Manifest{}, "", errors.New("投稿包包含不安全路径。")
		}
		files[name] = file
	}
	manifestFile := files["aishop.json"]
	if manifestFile == nil {
		return store.Manifest{}, "", errors.New("投稿包必须包含 aishop.json。")
	}
	var manifest store.Manifest
	if err := readJSONFromZip(manifestFile, &manifest); err != nil {
		return store.Manifest{}, "", errors.New("aishop.json 不是合法 JSON。")
	}
	if manifest.ID == "" || manifest.Name == "" || manifest.Version == "" || manifest.Summary == "" {
		return store.Manifest{}, "", errors.New("id、name、version、summary 必填。")
	}
	if manifest.Install == "" {
		manifest.Install = "install.ps1"
	}
	if files[manifest.Install] == nil {
		return store.Manifest{}, "", errors.New("投稿包必须包含 install.ps1。")
	}
	if manifest.Uninstall != "" && files[manifest.Uninstall] == nil {
		return store.Manifest{}, "", errors.New("声明了 uninstall 脚本，但文件不存在。")
	}
	if manifest.Update != "" && files[manifest.Update] == nil {
		return store.Manifest{}, "", errors.New("声明了 update 脚本，但文件不存在。")
	}
	changelogFile := files["CHANGELOG.txt"]
	if changelogFile == nil {
		return store.Manifest{}, "", errors.New("投稿包必须包含 CHANGELOG.txt。")
	}
	changelog, err := readTextFromZip(changelogFile, 1<<20)
	if err != nil {
		return store.Manifest{}, "", errors.New("读取 CHANGELOG.txt 失败。")
	}
	changelogBlock, ok := changelogForVersion(changelog, manifest.Version)
	if !ok {
		return store.Manifest{}, "", errors.New("CHANGELOG.txt 必须包含当前版本，格式为 === 版本号 | 日期 ===。")
	}
	return manifest, changelogBlock, nil
}

var changelogHeader = regexp.MustCompile(`(?m)^===\s*([^\|]+?)\s*\|\s*(\d{4}-\d{2}-\d{2})\s*===$`)

func changelogForVersion(text, version string) (string, bool) {
	matches := changelogHeader.FindAllStringSubmatchIndex(text, -1)
	for i, match := range matches {
		versionStart := match[2]
		versionEnd := match[3]
		dateStart := match[4]
		dateEnd := match[5]
		if strings.TrimSpace(text[versionStart:versionEnd]) != version {
			continue
		}
		if _, err := time.Parse("2006-01-02", text[dateStart:dateEnd]); err != nil {
			return "", false
		}
		bodyStart := match[1]
		bodyEnd := len(text)
		if i+1 < len(matches) {
			bodyEnd = matches[i+1][0]
		}
		body := strings.TrimSpace(text[bodyStart:bodyEnd])
		return body, body != ""
	}
	return "", false
}

func readJSONFromZip(file *zip.File, target any) error {
	reader, err := file.Open()
	if err != nil {
		return err
	}
	defer reader.Close()
	return json.NewDecoder(io.LimitReader(reader, 1<<20)).Decode(target)
}

func readTextFromZip(file *zip.File, limit int64) (string, error) {
	reader, err := file.Open()
	if err != nil {
		return "", err
	}
	defer reader.Close()
	data, err := io.ReadAll(io.LimitReader(reader, limit))
	return string(data), err
}

func validateAccount(w http.ResponseWriter, username, nickname, password string) bool {
	if strings.TrimSpace(username) == "" || len([]rune(username)) > 20 {
		fail(w, http.StatusBadRequest, "用户名最多 20 个字符。")
		return false
	}
	if strings.TrimSpace(nickname) == "" || len([]rune(nickname)) > 10 {
		fail(w, http.StatusBadRequest, "昵称最多 10 个字符。")
		return false
	}
	if len(password) < 6 || len(password) > 20 {
		fail(w, http.StatusBadRequest, "密码必须为 6 到 20 个字符。")
		return false
	}
	return true
}

func decode(w http.ResponseWriter, r *http.Request, target any) bool {
	if err := json.NewDecoder(r.Body).Decode(target); err != nil {
		fail(w, http.StatusBadRequest, "请求内容格式不正确。")
		return false
	}
	return true
}

func writeJSON(w http.ResponseWriter, value any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	_ = json.NewEncoder(w).Encode(value)
}

func fail(w http.ResponseWriter, status int, message string) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(map[string]string{"Error": message, "error": message})
}

func methodNotAllowed(w http.ResponseWriter) {
	fail(w, http.StatusMethodNotAllowed, "请求方法不正确。")
}

func splitPath(value string) []string {
	raw := strings.Split(strings.Trim(value, "/"), "/")
	var parts []string
	for _, part := range raw {
		if part != "" {
			parts = append(parts, part)
		}
	}
	return parts
}

func safeName(value string) string {
	value = strings.ReplaceAll(value, "\\", "_")
	value = strings.ReplaceAll(value, "/", "_")
	value = strings.ReplaceAll(value, "..", "_")
	return value
}

func copyFile(src, dst string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()
	out, err := os.Create(dst)
	if err != nil {
		return err
	}
	defer out.Close()
	_, err = io.Copy(out, in)
	return err
}

func cors(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Access-Control-Allow-Origin", "*")
		w.Header().Set("Access-Control-Allow-Headers", "Authorization, Content-Type")
		w.Header().Set("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
		if r.Method == http.MethodOptions {
			w.WriteHeader(http.StatusNoContent)
			return
		}
		next.ServeHTTP(w, r)
	})
}
