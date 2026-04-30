import { useEffect, useState } from 'react';

interface JobResult {
  title: string;
  company: string;
  logoUrl?: string;
  location: string;
  isRemote: boolean;
  employmentType?: string;
  minSalary?: string;
  maxSalary?: string;
  salaryCurrency?: string;
  salaryPeriod?: string;
  descriptionSnippet?: string;
  applyLink: string;
  postedAt?: string;
}

interface JobSearchResponse {
  totalResults: number;
  page: number;
  jobs: JobResult[];
}

const EMPLOYMENT_TYPES = [
  { label: 'Any type', value: '' },
  { label: 'Full-time', value: 'FULLTIME' },
  { label: 'Part-time', value: 'PARTTIME' },
  { label: 'Contract', value: 'CONTRACTOR' },
  { label: 'Internship', value: 'INTERN' }
];

const API_BASE = import.meta.env.VITE_API_BASE ?? '';

function formatSalary(job: JobResult): string | null {
  if (!job.minSalary && !job.maxSalary) return null;
  const currency = job.salaryCurrency ?? '';
  const period = job.salaryPeriod ? `/${job.salaryPeriod.toLowerCase()}` : '';
  if (job.minSalary && job.maxSalary)
    return `${currency} ${job.minSalary}–${job.maxSalary}${period}`;
  if (job.minSalary) return `${currency} ${job.minSalary}+${period}`;
  return `Up to ${currency} ${job.maxSalary}${period}`;
}

export default function Jobs({ userId, initialJobs, initialLabel, defaultLocation }: { userId?: string; initialJobs?: JobResult[]; initialLabel?: string; defaultLocation?: string }) {
  const [query, setQuery] = useState('');
  const [location, setLocation] = useState(defaultLocation ?? '');
  const [remoteOnly, setRemoteOnly] = useState(false);
  const [employmentType, setEmploymentType] = useState('');
  const [page, setPage] = useState(1);
  const [allJobs, setAllJobs] = useState<JobResult[]>(initialJobs ?? []);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hasSearched, setHasSearched] = useState((initialJobs?.length ?? 0) > 0);
  const [searchLabel, setSearchLabel] = useState<string | null>(initialLabel ?? null);

  useEffect(() => {
    if (!userId && (initialJobs?.length ?? 0) > 0) {
      setAllJobs(initialJobs!);
      setHasSearched(true);
      setSearchLabel(initialLabel ?? null);
    }
  }, [initialJobs, initialLabel, userId]);

  useEffect(() => {
    if (defaultLocation && !location) setLocation(defaultLocation);
  }, [defaultLocation, location]);

  useEffect(() => {
    if (!userId || !initialLabel) return;

    let cancelled = false;
    setLoading(true);
    setError(null);

    fetch(`${API_BASE}/api/jobs/recommended?userId=${encodeURIComponent(userId)}`)
      .then(res => {
        if (!res.ok) throw new Error(`Server error: ${res.status}`);
        return res.json();
      })
      .then(data => {
        if (cancelled) return;
        setAllJobs(data.jobs ?? []);
        setHasSearched(true);
        setSearchLabel(initialLabel);
        setPage(1);
      })
      .catch(() => {
        if (!cancelled) {
          setError('Could not load recommended jobs. Please try again.');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [userId, initialLabel]);

  // Client-side filtering — no new API call needed
  const filteredJobs = allJobs.filter(job => {
    if (remoteOnly && !job.isRemote) return false;
    if (employmentType) {
      const norm = (s: string) => s.toUpperCase().replace(/[^A-Z]/g, '');
      if (!norm(job.employmentType ?? '').includes(norm(employmentType))) return false;
    }
    return true;
  });

  async function search(pageNum = 1) {
    setLoading(true);
    setError(null);
    setHasSearched(true);
    setSearchLabel(null);

    try {
      const params = new URLSearchParams({
        query: query.trim() || 'software engineer',
        location: location.trim(),
        page: String(pageNum),
        remoteOnly: String(remoteOnly),
        ...(employmentType ? { employmentType } : {}),
        ...(userId ? { userId } : {}),
      });

      const res = await fetch(`${API_BASE}/api/jobs/search?${params}`);
      if (!res.ok) throw new Error(`Server error: ${res.status}`);
      const data: JobSearchResponse = await res.json();
      setAllJobs(data.jobs);
      setPage(pageNum);
    } catch {
      setError('Could not load jobs. Make sure your JSEARCH_API_KEY is set and the API is running.');
    } finally {
      setLoading(false);
    }
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    search(1);
  }

  return (
    <section className="jobs-shell">
      {/* Hero + search */}
      <div className="card jobs-hero">
        <div>
          <p className="section-title">Jobs Board</p>
          <h2 style={{ marginBottom: 8 }}>Find your next role</h2>
          <p style={{ color: '#9db7ff', maxWidth: 480 }}>
            Live job listings sourced from LinkedIn, Indeed, Glassdoor, and more — filtered for
            tech roles that match your field.
          </p>
        </div>

        <form className="jobs-search-form" onSubmit={handleSubmit}>
          <div className="jobs-search-inputs">
            <input
              type="text"
              className="jobs-input"
              placeholder="Role or keywords (e.g. Senior React Developer)"
              value={query}
              onChange={e => setQuery(e.target.value)}
            />
            <input
              type="text"
              className="jobs-input"
              placeholder="Location (e.g. Austin TX) or leave blank"
              value={location}
              onChange={e => setLocation(e.target.value)}
              disabled={remoteOnly}
            />
          </div>
          <button type="submit" className="primary-action jobs-search-btn" disabled={loading}>
            {loading ? 'Searching…' : 'Search Jobs'}
          </button>
        </form>
      </div>

      <div className="resources-layout">
        {/* Filters sidebar */}
        <aside className="resources-sidebar card">
          <p className="section-title">Filters</p>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
            <div>
              <p style={{ fontSize: '0.8rem', color: '#7c91c1', textTransform: 'uppercase', letterSpacing: '0.15em', marginBottom: 10 }}>
                Work type
              </p>
              <label className="jobs-toggle">
                <input
                  type="checkbox"
                  checked={remoteOnly}
                  onChange={e => {
                    setRemoteOnly(e.target.checked);
                    if (e.target.checked) setLocation('');
                  }}
                />
                <span>Remote only</span>
              </label>
            </div>

            <div>
              <p style={{ fontSize: '0.8rem', color: '#7c91c1', textTransform: 'uppercase', letterSpacing: '0.15em', marginBottom: 10 }}>
                Employment type
              </p>
              <div className="filter-chips">
                {EMPLOYMENT_TYPES.map(opt => (
                  <button
                    key={opt.value}
                    type="button"
                    className={`filter-chip ${employmentType === opt.value ? 'active' : ''}`}
                    onClick={() => setEmploymentType(opt.value)}
                    aria-pressed={employmentType === opt.value}
                  >
                    {opt.label}
                  </button>
                ))}
              </div>
            </div>
          </div>

          {hasSearched && allJobs.length > 0 && (
            <div className="industry-insight" style={{ marginTop: 8 }}>
              <div className="industry-signal">{filteredJobs.length} results</div>
              <p>{filteredJobs.length < allJobs.length ? `Filtered from ${allJobs.length} total` : `Page ${page}`}</p>
            </div>
          )}
        </aside>

        {/* Results */}
        <div className="jobs-content">
          {!hasSearched && (
            <div className="card jobs-prompt">
              <p className="section-title">Ready to search</p>
              <h3>Enter a role or keywords above to find live listings</h3>
              <p style={{ color: '#8ea5d9', marginTop: 8 }}>
                Try "Senior Data Engineer remote", "React developer Austin", or "Security analyst"
              </p>
            </div>
          )}

          {loading && (
            <div className="card jobs-state">
              <div className="chat-loading">
                <div className="spinner" />
                <span>Searching live listings…</span>
              </div>
            </div>
          )}

          {error && !loading && (
            <div className="alert error">{error}</div>
          )}

          {!loading && hasSearched && filteredJobs.length === 0 && (
            <div className="card jobs-state">
              <p className="section-title">No results</p>
              <h3>{allJobs.length > 0 ? 'No listings match the active filters' : 'No listings matched your search'}</h3>
              <p style={{ color: '#8ea5d9', marginTop: 8 }}>
                {allJobs.length > 0 ? 'Try clearing a filter to see more results.' : 'Try broader keywords or a different location.'}
              </p>
            </div>
          )}

          {!loading && filteredJobs.length > 0 && (
            <>
              {searchLabel && (
                <div className="card" style={{ padding: '12px 20px', marginBottom: 12, display: 'flex', alignItems: 'center', gap: 10 }}>
                  <span className="badge">AI Picks</span>
                  <span style={{ color: '#8ea5d9', fontSize: '0.9rem' }}>{searchLabel}</span>
                </div>
              )}
              <div className="jobs-list">
                {filteredJobs.map((job, i) => {
                  const salary = formatSalary(job);
                  return (
                    <div className="card job-card" key={`${job.company}-${job.title}-${i}`}>
                      <div className="job-card-top">
                        {job.logoUrl && (
                          <img
                            src={job.logoUrl}
                            alt={`${job.company} logo`}
                            className="job-logo"
                            onError={e => { (e.target as HTMLImageElement).style.display = 'none'; }}
                          />
                        )}
                        <div className="job-card-titles">
                          <h3 className="job-title">{job.title}</h3>
                          <p className="job-company">{job.company}</p>
                        </div>
                      </div>

                      <div className="job-card-meta">
                        <span className="pill subtle">{job.location}</span>
                        {job.isRemote && <span className="pill">Remote</span>}
                        {job.employmentType && (
                          <span className="pill subtle">
                            {job.employmentType.replace('_', ' ').toLowerCase().replace(/^\w/, c => c.toUpperCase())}
                          </span>
                        )}
                        {salary && <span className="job-salary">{salary}</span>}
                      </div>

                      {job.descriptionSnippet && (
                        <p className="job-snippet">{job.descriptionSnippet}</p>
                      )}

                      <div className="job-card-footer">
                        {job.postedAt && (
                          <span className="job-posted">Posted {job.postedAt}</span>
                        )}
                        <a
                          href={job.applyLink}
                          target="_blank"
                          rel="noreferrer"
                          className="job-apply-btn"
                        >
                          Apply →
                        </a>
                      </div>
                    </div>
                  );
                })}
              </div>

              <div className="jobs-pagination">
                <button
                  type="button"
                  className="ghost-button"
                  disabled={page <= 1}
                  onClick={() => search(page - 1)}
                >
                  ← Previous
                </button>
                <span className="jobs-page-indicator">Page {page}</span>
                <button
                  type="button"
                  className="ghost-button"
                  disabled={allJobs.length < 10}
                  onClick={() => search(page + 1)}
                >
                  Next →
                </button>
              </div>
            </>
          )}
        </div>
      </div>
    </section>
  );
}
