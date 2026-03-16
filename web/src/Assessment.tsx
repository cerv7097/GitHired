import { useMemo, useState } from 'react';

type Stage = 'selection' | 'in-progress' | 'summary';

export type TrackId =
  | 'software-development'
  | 'platforms-ops'
  | 'data-ai'
  | 'security'
  | 'product-design'
  | 'customer-solutions'
  | 'enterprise-domain'
  | 'unknown';

interface AssessmentTrack {
  id: TrackId;
  title: string;
  description: string;
  duration: string;
  focusAreas: string[];
  questions: string[];
  followUp: string;
  nextSteps: string[];
}

const TRACKS: AssessmentTrack[] = [
  {
    id: 'software-development',
    title: 'Software Development & Applications',
    description: 'Gauge your end-to-end product delivery skills spanning front-end, APIs, QA, and release health.',
    duration: '10 min · 9 questions',
    focusAreas: ['Full-stack execution', 'Code quality', 'Product delivery'],
    questions: [
      'Design maintainable front-end architectures that scale to new product surfaces.',
      'Build and document secure REST or GraphQL APIs that other teams can consume.',
      'Establish automated testing patterns (unit, integration, e2e) for complex services.',
      'Break down ambiguous product requirements into shippable engineering plans.',
      'Optimize application performance by profiling and eliminating bottlenecks.',
      'Collaborate with designers and PMs to ship cross-functional features.',
      'Review pull requests for architecture, readability, and team conventions.',
      'Instrument feature launches with telemetry to observe regressions.',
      'Mentor other engineers on patterns, tooling, or code reviews.'
    ],
    followUp: 'Translate usability feedback into technical tasks before the next sprint.',
    nextSteps: ['Deepen system design chops', 'Pair on observability improvements', 'Coach juniors through reviews']
  },
  {
    id: 'platforms-ops',
    title: 'Platforms and Operations',
    description: 'Assess how you design infrastructure, reliability guardrails, and automation ecosystems.',
    duration: '10 min · 8 questions',
    focusAreas: ['Cloud automation', 'Reliability SLAs', 'Tooling enablement'],
    questions: [
      'Design CI/CD pipelines that enforce quality gates and maintain fast feedback loops.',
      'Automate cloud infrastructure provisioning with Terraform, Bicep, or CloudFormation.',
      'Instrument alerting that balances signal-to-noise for on-call health.',
      'Drive cost optimization reviews across compute, storage, and data transfer.',
      'Create golden paths or internal platforms that reduce dev toil.',
      'Lead operational readiness reviews for large launches.',
      'Run incident retros with actionable follow-up work.',
      'Advocate for security, compliance, and policy-by-default in infra.',
      'Coach teams on observability tooling and operational best practices.'
    ],
    followUp: 'Map toil-heavy workflows and propose automation pilots.',
    nextSteps: ['Expand infrastructure-as-code coverage', 'Standardize runbooks', 'Measure mean time to recovery']
  },
  {
    id: 'data-ai',
    title: 'Data, AI, and Analytics',
    description: 'Understand your comfort across data modeling, ML experimentation, and analytics storytelling.',
    duration: '9 min · 8 questions',
    focusAreas: ['Analytics engineering', 'ML lifecycle', 'Experimentation'],
    questions: [
      'Model clean, tested datasets in dbt or similar transformation layers.',
      'Design A/B or multivariate experiments with proper guardrails.',
      'Build end-to-end data pipelines that meet SLAs for freshness.',
      'Select and evaluate ML models with the right metrics for the problem.',
      'Deploy and monitor ML models to prevent drift or quality erosion.',
      'Partner with stakeholders to translate ambiguous questions into analysis.',
      'Visualize complex data in dashboards or narratives tailored to execs.',
      'Maintain data governance, privacy, and catalog practices.'
    ],
    followUp: 'Tighten ML observability and alerting on business KPIs.',
    nextSteps: ['Ship a new analytics artifact', 'Harden feature stores', 'Document experiment learnings']
  },
  {
    id: 'security',
    title: 'Security',
    description: 'Evaluate how you secure applications, infrastructure, and company-wide policies.',
    duration: '8 min · 7 questions',
    focusAreas: ['Threat modeling', 'AppSec reviews', 'Governance'],
    questions: [
      'Lead threat modeling sessions for new initiatives.',
      'Embed secure coding practices into SDLC tooling.',
      'Design identity and access controls aligned with least privilege.',
      'Manage vulnerability triage and remediation cadences.',
      'Communicate risk posture and recommendations to leadership.',
      'Run tabletop exercises or incident simulations.',
      'Track compliance obligations (SOC 2, ISO, HIPAA, etc.).',
      'Educate teams through security champions or enablement.'
    ],
    followUp: 'Prioritize the riskiest gaps and create a mitigation backlog.',
    nextSteps: ['Refresh threat models', 'Expand automation in code scanning', 'Tighten identity governance']
  },
  {
    id: 'product-design',
    title: 'Product Design & Delivery',
    description: 'Measure how you connect user insights to prototypes, design systems, and launch excellence.',
    duration: '9 min · 8 questions',
    focusAreas: ['User research', 'Interaction design', 'Outcome alignment'],
    questions: [
      'Synthesize qualitative and quantitative research into design problems.',
      'Prototype flows rapidly to validate riskiest assumptions.',
      'Build reusable design system components with documentation.',
      'Run inclusive design critiques and incorporate feedback.',
      'Champion accessibility standards from concept to build.',
      'Partner with engineering to scope feasible MVP iterations.',
      'Define success metrics for design launches and track adoption.',
      'Tell compelling stories that influence roadmap priorities.'
    ],
    followUp: 'Ship a usability test plan with next sprint milestones.',
    nextSteps: ['Refresh journey maps', 'Audit design debt', 'Partner closely with research ops']
  },
  {
    id: 'customer-solutions',
    title: 'Customer Solutions',
    description: 'Score your ability to implement, support, and grow customer-facing technology programs.',
    duration: '8 min · 7 questions',
    focusAreas: ['Implementation', 'Customer success', 'Technical advisory'],
    questions: [
      'Lead discovery to map client goals, constraints, and success metrics.',
      'Configure or script integrations tailored to customer workflows.',
      'Run enablement sessions that leave admins self-sufficient.',
      'Translate complex issues into clear action plans for clients.',
      'Influence product roadmap with synthesized customer signals.',
      'Manage escalations calmly while coordinating internal teams.',
      'Identify expansion opportunities rooted in delivered value.',
      'Document playbooks that scale best practices across accounts.'
    ],
    followUp: 'Operationalize a voice-of-customer loop with product and sales.',
    nextSteps: ['Refresh onboarding templates', 'Tighten SLAs', 'Co-build success plans with clients']
  },
  {
    id: 'enterprise-domain',
    title: 'Enterprise & Domain Tech',
    description: 'Highlight your expertise implementing verticalized solutions and integrating enterprise systems across healthcare, fintech, govtech, and beyond.',
    duration: '8 min · 7 questions',
    focusAreas: ['Domain expertise', 'Enterprise integration', 'Regulatory compliance'],
    questions: [
      'Navigate regulatory or compliance requirements unique to the industry.',
      'Translate domain pain points into tailored product requirements.',
      'Integrate partner or legacy systems without disrupting operations.',
      'Drive adoption plans that respect frontline realities.',
      'Measure outcomes tied to industry benchmarks or mandates.',
      'Influence go-to-market messaging with domain credibility.',
      'Educate cross-functional teams on vocabulary, personas, and nuances.'
    ],
    followUp: 'Map the next certification or domain proof point that builds credibility in your vertical.',
    nextSteps: ['Document domain checklists', 'Earn a vertical-specific credential', 'Mentor peers on industry vocabulary']
  },
  {
    id: 'unknown',
    title: 'Unknown (Recommend a field to me)',
    description: 'Not sure where to focus? Answer diagnostic questions so we can recommend a high-impact path.',
    duration: '6 min · 6 questions',
    focusAreas: ['Discovery', 'Experimentation', 'Learning agility'],
    questions: [
      'I enjoy building or prototyping tangible solutions quickly.',
      'I like stabilizing complex systems or improving operational rigor.',
      'I gravitate toward data-heavy analysis and experimentation.',
      'I thrive when partnering with customers and translating their needs.',
      'I prefer crafting narratives, visuals, or experience flows.',
      'I feel energized by enterprise systems, vertical tech, or regulated domain problem spaces.'
    ],
    followUp: 'Use these signals to explore two tracks with mock projects.',
    nextSteps: ['Set up informational interviews', 'Shadow a peer in your top track', 'Prototype a small project in that lane']
  }
];

const TRACK_MAP = TRACKS.reduce<Record<TrackId, AssessmentTrack>>((acc, track) => {
  acc[track.id] = track;
  return acc;
}, {} as Record<TrackId, AssessmentTrack>);

const RATING_STEPS = [0, 1, 2, 3, 4] as const;
type RatingValue = (typeof RATING_STEPS)[number];
type ResponsesState = Partial<Record<number, RatingValue>>;

const RATING_LABELS: Record<RatingValue, string> = {
  0: 'New to this',
  1: 'Some exposure',
  2: 'Emerging confidence',
  3: 'Confident contributor',
  4: 'Go-to expert'
};

const BUCKETS = [
  { label: 'Core Execution', description: 'Hands-on ability to deliver high-quality work consistently.' },
  { label: 'Systems & Strategy', description: 'Zooming out to architecture, operations, or experimentation.' },
  { label: 'Collaboration & Influence', description: 'How you align teams, ship outcomes, and tell the story.' }
];

export default function Assessment() {
  const [stage, setStage] = useState<Stage>('selection');
  const [selectedTrack, setSelectedTrack] = useState<TrackId | null>(null);
  const [responses, setResponses] = useState<ResponsesState>({});

  const track = selectedTrack ? TRACK_MAP[selectedTrack] : null;
  const questions = track?.questions ?? [];
  const totalAnswered = questions.filter((_, idx) => responses[idx] !== undefined).length;
  const canComplete = stage === 'in-progress' && totalAnswered === questions.length && questions.length > 0;

  const totalPossible = questions.length * 4 || 1;
  const totalScore = questions.reduce((sum, _, idx) => sum + (responses[idx] ?? 0), 0);
  const scorePercent = Math.round((totalScore / totalPossible) * 100);

  const questionRatings = questions.map((question, index) => ({
    question,
    rating: responses[index] ?? null
  }));

  const strengths = questionRatings.filter(item => (item.rating ?? -1) >= 3).map(item => item.question);
  const growthAreas = questionRatings.filter(item => (item.rating ?? 5) <= 1).map(item => item.question);

  const bucketSize = Math.ceil((questions.length || 1) / BUCKETS.length);
  const breakdown = BUCKETS.map((bucket, bucketIndex) => {
    const start = bucketIndex * bucketSize;
    const subset = questionRatings.slice(start, start + bucketSize);
    const bucketPossible = subset.length * 4 || 1;
    const bucketScore = subset.reduce((sum, item) => sum + (item.rating ?? 0), 0);
    return {
      ...bucket,
      score: Math.round((bucketScore / bucketPossible) * 100)
    };
  });

  const scoreStory = useMemo(() => {
    if (!track) return '';
    if (scorePercent >= 85) {
      return `You are operating at an advanced level in ${track.title}. Double down on leading initiatives and mentoring others.`;
    }
    if (scorePercent >= 65) {
      return `Solid mid-level readiness detected for ${track.title}. Focus on sharpening two focus areas to unlock senior-level impact.`;
    }
    return `Foundational skills are forming. Anchor on the suggested next steps to build momentum in ${track.title}.`;
  }, [scorePercent, track]);

  const handleSelectTrack = (trackId: TrackId) => {
    setSelectedTrack(trackId);
    setResponses({});
    setStage('in-progress');
  };

  const handleRateQuestion = (questionIndex: number, rating: RatingValue) => {
    setResponses(prev => ({ ...prev, [questionIndex]: rating }));
  };

  const handleCompleteAssessment = () => {
    if (!canComplete) return;
    setStage('summary');
  };

  const handleRetake = () => {
    setResponses({});
    setStage('in-progress');
  };

  const handleChooseAnother = () => {
    setResponses({});
    setSelectedTrack(null);
    setStage('selection');
  };

  return (
    <section className="assessment-shell">
      <div className="card assessment-hero">
        <div>
          <p className="section-title">Skills Assessment</p>
          <h2 style={{ marginBottom: 8 }}>Assessment Lab</h2>
          <p style={{ color: '#8ea5d9', maxWidth: 540 }}>
            Select a capability track, rate your confidence from 0-4, and unlock a personalized readiness score with
            next-step coaching. Come back anytime to compare progress.
          </p>
        </div>
        <div className="assessment-stage-indicator">
          {['selection', 'in-progress', 'summary'].map(step => (
            <span key={step} className={stage === step ? 'active' : ''}>
              {step === 'selection' && 'Choose Track'}
              {step === 'in-progress' && 'Assessment'}
              {step === 'summary' && 'Insights'}
            </span>
          ))}
        </div>
      </div>

      {stage === 'selection' && (
        <div className="card">
          <div className="assessment-grid">
            {TRACKS.map(trackOption => (
              <button
                key={trackOption.id}
                type="button"
                className="assessment-track-card"
                onClick={() => handleSelectTrack(trackOption.id)}
              >
                <div className="assessment-track-meta">
                  <span className="pill">{trackOption.duration}</span>
                  <span className="pill subtle">{trackOption.focusAreas[0]}</span>
                </div>
                <h3>{trackOption.title}</h3>
                <p>{trackOption.description}</p>
                <ul>
                  {trackOption.focusAreas.map(area => (
                    <li key={area}>{area}</li>
                  ))}
                </ul>
                <div className="assessment-track-footer">
                  <span>Start Assessment</span>
                  <span aria-hidden>→</span>
                </div>
              </button>
            ))}
          </div>
        </div>
      )}

      {stage === 'in-progress' && track && (
        <div className="card assessment-stage-card">
          <header className="assessment-stage-header">
            <div>
              <p className="section-title">In Progress</p>
              <h3>{track.title}</h3>
              <p className="assessment-stage-subtitle">{track.description}</p>
            </div>
            <div className="assessment-stage-progress">
              <span>
                {totalAnswered}/{questions.length} answered
              </span>
              <div className="assessment-progress-bar">
                <span style={{ width: `${(totalAnswered / (questions.length || 1)) * 100}%` }} />
              </div>
            </div>
          </header>

          <ol className="assessment-question-list">
            {questionRatings.map((item, index) => (
              <li key={item.question}>
                <div className="question-header">
                  <span className="question-index">{index + 1}</span>
                  <p>{item.question}</p>
                </div>
                <div className="rating-scale">
                  {RATING_STEPS.map(step => (
                    <button
                      key={step}
                      type="button"
                      className={item.rating === step ? 'selected' : ''}
                      onClick={() => handleRateQuestion(index, step)}
                    >
                      {step}
                    </button>
                  ))}
                </div>
                <small className="rating-hint">
                  {item.rating === null ? 'Select your comfort level' : RATING_LABELS[item.rating]}
                </small>
              </li>
            ))}
          </ol>

          <footer className="assessment-stage-actions">
            <button type="button" className="ghost-button" onClick={handleChooseAnother}>
              Back to tracks
            </button>
            <button type="button" className="assessment-primary" disabled={!canComplete} onClick={handleCompleteAssessment}>
              View insights
            </button>
          </footer>
        </div>
      )}

      {stage === 'summary' && track && (
        <div className="card assessment-summary-card">
          <div className="assessment-summary-hero">
            <div className="score-ring">
              <strong>{scorePercent}%</strong>
              <span>Overall readiness</span>
            </div>
            <div className="summary-copy">
              <p className="section-title">Track</p>
              <h3>{track.title}</h3>
              <p>{scoreStory}</p>
              <div className="pill-row">
                {track.focusAreas.map(area => (
                  <span key={area} className="pill subtle">
                    {area}
                  </span>
                ))}
              </div>
            </div>
          </div>

          <div className="assessment-breakdown">
            {breakdown.map(bucket => (
              <div key={bucket.label} className="breakdown-card">
                <div className="breakdown-score">{bucket.score}%</div>
                <p className="breakdown-label">{bucket.label}</p>
                <small>{bucket.description}</small>
              </div>
            ))}
          </div>

          <div className="assessment-strengths">
            <div>
              <h4>Strength signals</h4>
              {strengths.length === 0 ? (
                <p className="muted">No standout strengths yet. Complete more assessments to unlock insights.</p>
              ) : (
                <ul>
                  {strengths.map(item => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              )}
            </div>
            <div>
              <h4>Growth focus</h4>
              {growthAreas.length === 0 ? (
                <p className="muted">No urgent gaps highlighted. Keep stretching the bar.</p>
              ) : (
                <ul>
                  {growthAreas.map(item => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              )}
            </div>
            <div>
              <h4>Next moves</h4>
              <ul>
                {track.nextSteps.map(step => (
                  <li key={step}>{step}</li>
                ))}
              </ul>
              <p className="muted">{track.followUp}</p>
            </div>
          </div>

          <div className="assessment-question-review">
            <h4>Question review</h4>
            {questionRatings.map(item => (
              <div key={item.question} className="question-review-row">
                <div>
                  <p>{item.question}</p>
                  <small>{item.rating === null ? 'No response recorded' : RATING_LABELS[item.rating]}</small>
                </div>
                <span className="badge">{item.rating ?? '—'}/4</span>
              </div>
            ))}
          </div>

          <div className="assessment-summary-actions">
            <button type="button" className="ghost-button" onClick={handleRetake}>
              Retake track
            </button>
            <button type="button" className="assessment-primary" onClick={handleChooseAnother}>
              Choose another track
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
