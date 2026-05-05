import { useEffect, useMemo, useState } from 'react';

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

/**
 * Each named track now classifies its questions into one of three buckets so the
 * summary breakdown reflects what the question actually measures, instead of
 * splitting the question list into equal index-chunks.
 */
type Bucket = 'core' | 'systems' | 'collaboration';

interface AssessmentQuestion {
  text: string;
  /** Bucket this question contributes to in the named-track breakdown. Ignored for 'unknown'. */
  bucket: Bucket;
  /** Concrete next-step shown in the Growth Focus list when the user rates this low. Optional for unknown. */
  growthHint?: string;
  /** Only set on 'unknown' track — which named track this preference question scores for. */
  mapsTo?: TrackId;
}

interface AssessmentTrack {
  id: TrackId;
  title: string;
  description: string;
  duration: string;
  focusAreas: string[];
  questions: AssessmentQuestion[];
  followUp: string;
  nextSteps: string[];
}

/**
 * Visual icon shown at the top of each track card on the selection screen so
 * users can scan and recognize tracks at a glance instead of reading 8 nearly
 * identical card titles. Also exported and reused by the Resources sidebar so
 * filtering across the app stays visually consistent.
 */
export const TRACK_ICONS: Record<TrackId, string> = {
  'software-development': '💻',
  'platforms-ops': '☁️',
  'data-ai': '📊',
  'security': '🛡️',
  'product-design': '✏️',
  'customer-solutions': '🤝',
  'enterprise-domain': '🏢',
  'unknown': '🧭'
};

const TRACKS: AssessmentTrack[] = [
  {
    id: 'software-development',
    title: 'Software Development & Applications',
    description: 'Gauge your end-to-end product delivery skills spanning front-end, APIs, QA, and release health.',
    duration: '10 min · 9 questions',
    focusAreas: ['Full-stack execution', 'Code quality', 'Product delivery'],
    questions: [
      { text: 'Design maintainable front-end architectures that scale to new product surfaces.', bucket: 'core',
        growthHint: 'Sketch a component architecture for one of your team\'s upcoming surfaces and walk a peer through it.' },
      { text: 'Build and document secure REST or GraphQL APIs that other teams can consume.', bucket: 'core',
        growthHint: 'Write API docs (OpenAPI/spec) for one existing endpoint and ship it to the consuming team.' },
      { text: 'Establish automated testing patterns (unit, integration, e2e) for complex services.', bucket: 'core',
        growthHint: 'Pick the riskiest untested area and add a layered test plan; aim for one new e2e per sprint.' },
      { text: 'Break down ambiguous product requirements into shippable engineering plans.', bucket: 'systems',
        growthHint: 'Take an open spec and write a one-page tech plan with milestones before the next planning meeting.' },
      { text: 'Optimize application performance by profiling and eliminating bottlenecks.', bucket: 'systems',
        growthHint: 'Profile one user-facing flow, ship one measurable perf win, and document the before/after.' },
      { text: 'Collaborate with designers and PMs to ship cross-functional features.', bucket: 'collaboration',
        growthHint: 'Volunteer to drive design/eng syncs for the next feature kickoff.' },
      { text: 'Review pull requests for architecture, readability, and team conventions.', bucket: 'collaboration',
        growthHint: 'Set a goal of reviewing 5 PRs/week with at least one architectural comment each.' },
      { text: 'Instrument feature launches with telemetry to observe regressions.', bucket: 'systems',
        growthHint: 'Add one launch dashboard before your next ship; tie it to a clear success metric.' },
      { text: 'Mentor other engineers on patterns, tooling, or code reviews.', bucket: 'collaboration',
        growthHint: 'Pair with a junior on one feature this sprint and share a written retro.' }
    ],
    followUp: 'Translate usability feedback into technical tasks before the next sprint.',
    nextSteps: ['Deepen system design chops', 'Pair on observability improvements', 'Coach juniors through reviews']
  },
  {
    id: 'platforms-ops',
    title: 'Platforms and Operations',
    description: 'Assess how you design infrastructure, reliability guardrails, and automation ecosystems.',
    duration: '10 min · 9 questions',
    focusAreas: ['Cloud automation', 'Reliability SLAs', 'Tooling enablement'],
    questions: [
      { text: 'Design CI/CD pipelines that enforce quality gates and maintain fast feedback loops.', bucket: 'core',
        growthHint: 'Add one quality gate (linting, security scan, or perf budget) to your team\'s pipeline this month.' },
      { text: 'Automate cloud infrastructure provisioning with Terraform, Bicep, or CloudFormation.', bucket: 'core',
        growthHint: 'Convert one manually-provisioned resource to IaC and submit it for review.' },
      { text: 'Instrument alerting that balances signal-to-noise for on-call health.', bucket: 'systems',
        growthHint: 'Audit your noisiest alert and either fix or delete it; share the result with on-call peers.' },
      { text: 'Drive cost optimization reviews across compute, storage, and data transfer.', bucket: 'systems',
        growthHint: 'Run a cost review on one service and propose two optimizations with projected savings.' },
      { text: 'Create golden paths or internal platforms that reduce dev toil.', bucket: 'systems',
        growthHint: 'Identify a repeated dev workflow and ship a script, template, or paved path for it.' },
      { text: 'Lead operational readiness reviews for large launches.', bucket: 'collaboration',
        growthHint: 'Volunteer as the ops lead for an upcoming launch; produce a checklist beforehand.' },
      { text: 'Run incident retros with actionable follow-up work.', bucket: 'collaboration',
        growthHint: 'Facilitate the next blameless retro and own the action-item tracking.' },
      { text: 'Advocate for security, compliance, and policy-by-default in infra.', bucket: 'systems',
        growthHint: 'Add one default policy (IAM least-privilege, secret rotation, etc.) to a new service template.' },
      { text: 'Coach teams on observability tooling and operational best practices.', bucket: 'collaboration',
        growthHint: 'Run a 30-minute internal session on one observability tool teams underuse.' }
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
      { text: 'Model clean, tested datasets in dbt or similar transformation layers.', bucket: 'core',
        growthHint: 'Refactor one fragile transformation into tested dbt models with tests for nulls and uniqueness.' },
      { text: 'Design A/B or multivariate experiments with proper guardrails.', bucket: 'systems',
        growthHint: 'Write the experiment spec for an upcoming launch including power calc and stop-conditions.' },
      { text: 'Build end-to-end data pipelines that meet SLAs for freshness.', bucket: 'core',
        growthHint: 'Pick one pipeline missing freshness SLAs and add monitors plus on-call ownership.' },
      { text: 'Select and evaluate ML models with the right metrics for the problem.', bucket: 'core',
        growthHint: 'Re-evaluate one shipped model against a business metric, not just offline accuracy.' },
      { text: 'Deploy and monitor ML models to prevent drift or quality erosion.', bucket: 'systems',
        growthHint: 'Add a drift monitor to one production model and define an alert threshold.' },
      { text: 'Partner with stakeholders to translate ambiguous questions into analysis.', bucket: 'collaboration',
        growthHint: 'Take one half-formed business question, scope it into a 1-pager, and review with stakeholders.' },
      { text: 'Visualize complex data in dashboards or narratives tailored to execs.', bucket: 'collaboration',
        growthHint: 'Build one exec-ready dashboard with a written narrative; collect feedback before iterating.' },
      { text: 'Maintain data governance, privacy, and catalog practices.', bucket: 'systems',
        growthHint: 'Document ownership and PII tags for one critical table in your team\'s catalog.' }
    ],
    followUp: 'Tighten ML observability and alerting on business KPIs.',
    nextSteps: ['Ship a new analytics artifact', 'Harden feature stores', 'Document experiment learnings']
  },
  {
    id: 'security',
    title: 'Security',
    description: 'Evaluate how you secure applications, infrastructure, and company-wide policies.',
    duration: '8 min · 8 questions',
    focusAreas: ['Threat modeling', 'AppSec reviews', 'Governance'],
    questions: [
      { text: 'Lead threat modeling sessions for new initiatives.', bucket: 'systems',
        growthHint: 'Facilitate a STRIDE threat-modeling session on the next major design and document the findings.' },
      { text: 'Embed secure coding practices into SDLC tooling.', bucket: 'core',
        growthHint: 'Add one new secure-coding linter or SAST rule to your team\'s pre-commit or CI.' },
      { text: 'Design identity and access controls aligned with least privilege.', bucket: 'core',
        growthHint: 'Audit one role/service\'s permissions and propose a least-privilege rewrite.' },
      { text: 'Manage vulnerability triage and remediation cadences.', bucket: 'core',
        growthHint: 'Set up a weekly triage with explicit SLAs by severity, and report adherence monthly.' },
      { text: 'Communicate risk posture and recommendations to leadership.', bucket: 'collaboration',
        growthHint: 'Draft a one-page risk brief for leadership covering top 3 risks and recommended mitigations.' },
      { text: 'Run tabletop exercises or incident simulations.', bucket: 'collaboration',
        growthHint: 'Schedule a tabletop with on-call covering one realistic breach scenario.' },
      { text: 'Track compliance obligations (SOC 2, ISO, HIPAA, etc.).', bucket: 'systems',
        growthHint: 'Map one compliance control to its evidence collection and identify the gap.' },
      { text: 'Educate teams through security champions or enablement.', bucket: 'collaboration',
        growthHint: 'Run a 20-minute brown-bag on a recent CVE relevant to your stack.' }
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
      { text: 'Synthesize qualitative and quantitative research into design problems.', bucket: 'systems',
        growthHint: 'Pair a quant funnel chart with two qual quotes to define your next design problem.' },
      { text: 'Prototype flows rapidly to validate riskiest assumptions.', bucket: 'core',
        growthHint: 'Build one quick prototype to test the riskiest assumption in your current project.' },
      { text: 'Build reusable design system components with documentation.', bucket: 'core',
        growthHint: 'Promote one one-off component into the design system with usage docs.' },
      { text: 'Run inclusive design critiques and incorporate feedback.', bucket: 'collaboration',
        growthHint: 'Host a critique with at least one engineer and one PM and capture decisions in writing.' },
      { text: 'Champion accessibility standards from concept to build.', bucket: 'core',
        growthHint: 'Audit one shipped flow against WCAG AA and file the top three fixes.' },
      { text: 'Partner with engineering to scope feasible MVP iterations.', bucket: 'collaboration',
        growthHint: 'Co-write the MVP scope for an upcoming feature with the engineering lead.' },
      { text: 'Define success metrics for design launches and track adoption.', bucket: 'systems',
        growthHint: 'Define one success metric pre-launch and set up the dashboard to track it post-launch.' },
      { text: 'Tell compelling stories that influence roadmap priorities.', bucket: 'collaboration',
        growthHint: 'Pitch one design opportunity with data + narrative at the next roadmap review.' }
    ],
    followUp: 'Ship a usability test plan with next sprint milestones.',
    nextSteps: ['Refresh journey maps', 'Audit design debt', 'Partner closely with research ops']
  },
  {
    id: 'customer-solutions',
    title: 'Customer Solutions',
    description: 'Score your ability to implement, support, and grow customer-facing technology programs.',
    duration: '8 min · 8 questions',
    focusAreas: ['Implementation', 'Customer success', 'Technical advisory'],
    questions: [
      { text: 'Lead discovery to map client goals, constraints, and success metrics.', bucket: 'systems',
        growthHint: 'Run a structured discovery session and write a one-pager goals/metrics summary for the client.' },
      { text: 'Configure or script integrations tailored to customer workflows.', bucket: 'core',
        growthHint: 'Script one repetitive integration step and share it as a reusable template across accounts.' },
      { text: 'Run enablement sessions that leave admins self-sufficient.', bucket: 'collaboration',
        growthHint: 'Build a 30-min admin enablement deck and run it with two clients this quarter.' },
      { text: 'Translate complex issues into clear action plans for clients.', bucket: 'collaboration',
        growthHint: 'For your next escalation, write a 5-bullet action plan and share with the client lead.' },
      { text: 'Influence product roadmap with synthesized customer signals.', bucket: 'systems',
        growthHint: 'Synthesize three recent customer signals into one product feedback brief.' },
      { text: 'Manage escalations calmly while coordinating internal teams.', bucket: 'collaboration',
        growthHint: 'Define an internal RACI for the next escalation and use it to drive alignment.' },
      { text: 'Identify expansion opportunities rooted in delivered value.', bucket: 'systems',
        growthHint: 'Document one realized customer outcome and propose the natural expansion play to your AM.' },
      { text: 'Document playbooks that scale best practices across accounts.', bucket: 'core',
        growthHint: 'Turn one repeated success into a playbook your team can reuse.' }
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
      { text: 'Navigate regulatory or compliance requirements unique to the industry.', bucket: 'systems',
        growthHint: 'Pick the most-relevant regulation in your vertical and map two upcoming features against it.' },
      { text: 'Translate domain pain points into tailored product requirements.', bucket: 'systems',
        growthHint: 'Write a domain-flavored problem statement based on your last 3 customer conversations.' },
      { text: 'Integrate partner or legacy systems without disrupting operations.', bucket: 'core',
        growthHint: 'Document the integration strategy for one legacy system before the next implementation.' },
      { text: 'Drive adoption plans that respect frontline realities.', bucket: 'collaboration',
        growthHint: 'Shadow a frontline user and revise an adoption plan based on what you observe.' },
      { text: 'Measure outcomes tied to industry benchmarks or mandates.', bucket: 'systems',
        growthHint: 'Choose one industry benchmark and instrument your product to measure against it.' },
      { text: 'Influence go-to-market messaging with domain credibility.', bucket: 'collaboration',
        growthHint: 'Co-write one piece of marketing collateral with your sales/marketing partner.' },
      { text: 'Educate cross-functional teams on vocabulary, personas, and nuances.', bucket: 'collaboration',
        growthHint: 'Run a 30-minute domain primer for new engineers/PMs joining the area.' }
    ],
    followUp: 'Map the next certification or domain proof point that builds credibility in your vertical.',
    nextSteps: ['Document domain checklists', 'Earn a vertical-specific credential', 'Mentor peers on industry vocabulary']
  },
  {
    id: 'unknown',
    title: 'Unknown (Recommend a field to me)',
    description: 'Not sure where to focus? Rate how strongly each work style resonates with you and we\'ll recommend the best-fit field.',
    duration: '5 min · 7 questions',
    focusAreas: ['Discovery', 'Field recommendation', 'Learning agility'],
    // Each preference question maps 1:1 to a named track. The summary view ranks tracks
    // by the user's rating of these questions instead of computing a generic "% readiness".
    questions: [
      { text: 'I enjoy building or prototyping tangible solutions quickly.',
        bucket: 'core', mapsTo: 'software-development' },
      { text: 'I like stabilizing complex systems or improving operational rigor.',
        bucket: 'core', mapsTo: 'platforms-ops' },
      { text: 'I gravitate toward data-heavy analysis and experimentation.',
        bucket: 'core', mapsTo: 'data-ai' },
      { text: 'I\'m drawn to defending systems, threat modeling, or governance work.',
        bucket: 'core', mapsTo: 'security' },
      { text: 'I prefer crafting narratives, visuals, or experience flows.',
        bucket: 'core', mapsTo: 'product-design' },
      { text: 'I thrive when partnering with customers and translating their needs.',
        bucket: 'core', mapsTo: 'customer-solutions' },
      { text: 'I feel energized by enterprise systems, vertical tech, or regulated domain problem spaces.',
        bucket: 'core', mapsTo: 'enterprise-domain' }
    ],
    followUp: 'Use these signals to explore your top track with a small mock project.',
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

const BUCKET_DEFS: Record<Bucket, { label: string; description: string }> = {
  core: {
    label: 'Hands-on Skills',
    description: 'The actual doing of the work — building, shipping, and the daily craft.'
  },
  systems: {
    label: 'Bigger-Picture Thinking',
    description: 'Architecture, strategy, and the zoomed-out design choices behind the work.'
  },
  collaboration: {
    label: 'Working with Others',
    description: 'How you mentor, align, and influence outcomes across teams.'
  }
};

const BUCKET_ORDER: Bucket[] = ['core', 'systems', 'collaboration'];

/**
 * Maps an overall readiness percentage to a plain-English skill tier so the
 * raw % isn't the only signal the user sees. Renders as a small badge below
 * the score ring on named-track summaries.
 */
function maturityFor(scorePercent: number): { label: string; description: string } {
  if (scorePercent >= 81) return { label: 'Advanced', description: 'You can lead and mentor others here.' };
  if (scorePercent >= 56) return { label: 'Proficient', description: 'You\'re a dependable contributor.' };
  if (scorePercent >= 31) return { label: 'Foundational', description: 'You can hold your own on the basics.' };
  return { label: 'Exploring', description: 'You\'re still getting your bearings here.' };
}

// IndustryId in App.tsx mirrors TrackId minus 'unknown'. We accept the broader
// TrackId here so the prop is easy to pass; the unknown track simply won't render
// the resources CTA (no resources are tagged for 'unknown').
type NamedTrackId = Exclude<TrackId, 'unknown'>;

interface AssessmentProps {
  /** Current user ID — used to fetch a personalized AI insight on the summary view. */
  userId?: string;
  /** Switches to the Resources tab and pre-filters to the given track. */
  onJumpToResources?: (trackId: NamedTrackId) => void;
  /** Pre-computed resource counts per track for the "Browse N resources" CTA copy. */
  resourceCountByTrack?: Partial<Record<NamedTrackId, number>>;
}

const API_BASE = import.meta.env.VITE_API_BASE ?? '';

export default function Assessment({ userId, onJumpToResources, resourceCountByTrack }: AssessmentProps = {}) {
  const [stage, setStage] = useState<Stage>('selection');
  const [selectedTrack, setSelectedTrack] = useState<TrackId | null>(null);
  const [responses, setResponses] = useState<ResponsesState>({});

  const track = selectedTrack ? TRACK_MAP[selectedTrack] : null;
  const questions = track?.questions ?? [];
  const totalAnswered = questions.filter((_, idx) => responses[idx] !== undefined).length;
  const canComplete = stage === 'in-progress' && totalAnswered === questions.length && questions.length > 0;
  const isUnknownTrack = track?.id === 'unknown';

  const totalPossible = questions.length * 4 || 1;
  const totalScore = questions.reduce((sum, _, idx) => sum + (responses[idx] ?? 0), 0);
  const scorePercent = Math.round((totalScore / totalPossible) * 100);

  const questionRatings = questions.map((question, index) => ({
    question,
    rating: responses[index] ?? null
  }));

  // Strengths surface in the celebratory "Where you're strong" section. Growth
  // areas were previously a separate column, but they were duplicating the
  // personalized item already prepended to Next moves, so the standalone list
  // was removed during the layout cleanup.
  const strengths = questionRatings
    .filter(item => (item.rating ?? -1) >= 3)
    .map(item => ({ text: item.question.text, hint: item.question.growthHint }));

  // Bucket breakdown — now driven by the explicit `bucket` tag on each question
  // rather than splitting the question list by index.
  const breakdown = useMemo(() => {
    return BUCKET_ORDER.map(bucket => {
      const subset = questionRatings.filter(item => item.question.bucket === bucket);
      const bucketPossible = subset.length * 4 || 1;
      const bucketScore = subset.reduce((sum, item) => sum + (item.rating ?? 0), 0);
      const score = subset.length === 0 ? null : Math.round((bucketScore / bucketPossible) * 100);
      return {
        bucket,
        label: BUCKET_DEFS[bucket].label,
        description: BUCKET_DEFS[bucket].description,
        score,
        questionCount: subset.length
      };
    }).filter(b => b.questionCount > 0);
  }, [questionRatings]);

  // For the 'unknown' track only — rank named tracks by the user's preference rating
  // and produce an alignment percentage per track. This replaces the meaningless
  // "% readiness" output that the unknown track previously inherited.
  const unknownRecommendations = useMemo(() => {
    if (!isUnknownTrack) return [] as Array<{ trackId: TrackId; title: string; alignment: number }>;
    return questionRatings
      .filter(item => item.question.mapsTo)
      .map(item => {
        const targetId = item.question.mapsTo as TrackId;
        const target = TRACK_MAP[targetId];
        // Each preference question is 0-4; map to 0-100% alignment.
        const alignment = Math.round(((item.rating ?? 0) / 4) * 100);
        return { trackId: targetId, title: target.title, alignment };
      })
      .sort((a, b) => b.alignment - a.alignment);
  }, [isUnknownTrack, questionRatings]);

  const topRecommendation = unknownRecommendations[0] ?? null;
  const runnerUp = unknownRecommendations[1] ?? null;

  // Personalized next moves: lead with the growth hint from the user's lowest-rated
  // question (the most concrete "do this next" suggestion in the whole assessment),
  // then fall back to the static nextSteps from the track config.
  const personalizedNextMoves = useMemo(() => {
    if (!track || isUnknownTrack) return [] as string[];
    const ratedLow = questionRatings
      .filter(item => item.rating !== null && item.rating <= 1 && item.question.growthHint)
      .sort((a, b) => (a.rating ?? 0) - (b.rating ?? 0));
    const personal = ratedLow.length > 0 ? [ratedLow[0].question.growthHint as string] : [];
    return [...personal, ...track.nextSteps];
  }, [track, isUnknownTrack, questionRatings]);

  // Skill maturity tier (only meaningful for named tracks; the unknown track shows
  // a recommendation, not a readiness score).
  const maturity = !isUnknownTrack ? maturityFor(scorePercent) : null;

  // Identify the weakest / strongest bucket so the new bar-chart can call them out
  // explicitly. We only annotate when there's a meaningful spread — if all buckets
  // tie, the labels would be misleading.
  const bucketAnnotations = useMemo(() => {
    const scored = breakdown.filter(b => b.score !== null) as Array<{ bucket: Bucket; score: number }>;
    const distinct = new Set(scored.map(b => b.score));
    if (scored.length < 2 || distinct.size < 2) {
      return { weakestBucket: null as Bucket | null, strongestBucket: null as Bucket | null };
    }
    const lowest = Math.min(...scored.map(b => b.score));
    const highest = Math.max(...scored.map(b => b.score));
    return {
      weakestBucket: (scored.find(b => b.score === lowest)?.bucket ?? null) as Bucket | null,
      strongestBucket: (scored.find(b => b.score === highest)?.bucket ?? null) as Bucket | null
    };
  }, [breakdown]);

  // Resources CTA — only shown for named tracks, only when at least one resource
  // is tagged for this track.
  const resourceCount = track && !isUnknownTrack
    ? resourceCountByTrack?.[track.id as NamedTrackId] ?? 0
    : 0;
  const showResourcesCta = !isUnknownTrack && resourceCount > 0 && onJumpToResources && track !== null;

  // ============ AI personalized insight ============
  // When the user reaches the summary stage on a named track, we POST their scores
  // + weakest/strongest questions to /api/assessment/insight. The endpoint combines
  // them with the user's stored profile (resume skills, roles, experience level) and
  // returns a 2-3 sentence coaching paragraph. If the call fails or returns null
  // (no key configured, network down, no profile, etc.) the section quietly hides
  // — the rest of the summary still works.
  const [aiInsight, setAiInsight] = useState<string | null>(null);
  const [insightLoading, setInsightLoading] = useState(false);

  useEffect(() => {
    if (stage !== 'summary' || !track || isUnknownTrack || !userId) {
      setAiInsight(null);
      setInsightLoading(false);
      return;
    }

    const coreScore = breakdown.find(b => b.bucket === 'core')?.score ?? null;
    const systemsScore = breakdown.find(b => b.bucket === 'systems')?.score ?? null;
    const collaborationScore = breakdown.find(b => b.bucket === 'collaboration')?.score ?? null;

    const strongestQuestions = questionRatings
      .filter(item => (item.rating ?? -1) >= 3)
      .slice(0, 3)
      .map(item => item.question.text);
    const weakestQuestions = questionRatings
      .filter(item => (item.rating ?? 5) <= 1)
      .slice(0, 3)
      .map(item => item.question.text);

    let cancelled = false;
    setInsightLoading(true);
    setAiInsight(null);

    fetch(`${API_BASE}/api/assessment/insight`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        userId,
        trackId: track.id,
        trackTitle: track.title,
        overallScore: scorePercent,
        coreScore,
        systemsScore,
        collaborationScore,
        strongestQuestions,
        weakestQuestions
      })
    })
      .then(res => res.ok ? res.json() : null)
      .then(data => {
        if (cancelled) return;
        const insight = (data && typeof data.insight === 'string') ? data.insight : null;
        setAiInsight(insight && insight.trim().length > 0 ? insight : null);
      })
      .catch(() => {
        // Silently swallow — section just doesn't render. The rest of the summary
        // is fully usable without the AI paragraph.
        if (!cancelled) setAiInsight(null);
      })
      .finally(() => {
        if (!cancelled) setInsightLoading(false);
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stage, track?.id, isUnknownTrack, userId]);

  const scoreStory = useMemo(() => {
    if (!track) return '';
    if (isUnknownTrack) {
      // Unknown track tells a recommendation story, not a readiness story.
      if (!topRecommendation || topRecommendation.alignment === 0) {
        return 'Try rating each prompt — your responses will surface the field that fits you best.';
      }
      if (runnerUp && Math.abs(topRecommendation.alignment - runnerUp.alignment) <= 12) {
        return `Your strongest signals are split between ${topRecommendation.title} and ${runnerUp.title}. Try a mock project in each to see which one keeps you energized.`;
      }
      return `Your strongest signal is ${topRecommendation.title}. Take that assessment next to get a real readiness score.`;
    }
    if (scorePercent >= 85) {
      return `You are operating at an advanced level in ${track.title}. Double down on leading initiatives and mentoring others.`;
    }
    if (scorePercent >= 65) {
      return `Solid mid-level readiness detected for ${track.title}. Focus on sharpening two focus areas to unlock senior-level impact.`;
    }
    return `Foundational skills are forming. Anchor on the suggested next steps to build momentum in ${track.title}.`;
  }, [scorePercent, track, isUnknownTrack, topRecommendation, runnerUp]);

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

  // Scroll to the top of the page whenever the assessment stage changes
  // (selection → in-progress → summary, or back). Without this, finishing an
  // assessment leaves the user mid-page on the results view.
  useEffect(() => {
    window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
  }, [stage]);

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
          {(['selection', 'in-progress', 'summary'] as Stage[]).map(step => {
            // Each chip is a navigation shortcut. We disable the ones that don't make
            // sense given current state — e.g. you can't jump to "Insights" before
            // finishing the questions, and you can't jump to "Assessment" without
            // first picking a track.
            const isCurrent = stage === step;
            const canNavigate =
              step === 'selection' ||
              (step === 'in-progress' && track !== null) ||
              (step === 'summary' && (canComplete || stage === 'summary'));

            const label =
              step === 'selection' ? 'Choose Track' :
              step === 'in-progress' ? 'Assessment' : 'Insights';

            const handleClick = () => {
              if (!canNavigate || isCurrent) return;
              if (step === 'selection') {
                handleChooseAnother();
              } else if (step === 'in-progress') {
                setStage('in-progress');
              } else {
                setStage('summary');
              }
            };

            return (
              <button
                key={step}
                type="button"
                className={isCurrent ? 'active' : ''}
                onClick={handleClick}
                disabled={!canNavigate || isCurrent}
                // Reset only the bits the user agent forces on <button> — borders
                // and font — so the .assessment-stage-indicator CSS can fully
                // control color, background, padding, and hover/active states.
                style={{ border: 'none', font: 'inherit' }}
                aria-label={`Go to ${label}`}
              >
                {label}
              </button>
            );
          })}
        </div>
      </div>

      {stage === 'selection' && (
        <div className="card">
          <div className="assessment-grid">
            {TRACKS.map(trackOption => (
              <button
                key={trackOption.id}
                type="button"
                className={`assessment-track-card${trackOption.id === 'unknown' ? ' assessment-track-card--unknown' : ''}`}
                onClick={() => handleSelectTrack(trackOption.id)}
              >
                <div className="assessment-track-icon" aria-hidden="true">
                  {TRACK_ICONS[trackOption.id]}
                </div>
                <h3>{trackOption.title}</h3>
                <p>{trackOption.description}</p>
                <div className="assessment-track-tags">
                  {trackOption.focusAreas.map(area => (
                    <span key={area} className="pill subtle small">{area}</span>
                  ))}
                </div>
                <div className="assessment-track-footer">
                  <span className="assessment-track-meta-text">
                    {trackOption.questions.length} prompts
                  </span>
                  <span className="assessment-track-cta">
                    Start <span aria-hidden>→</span>
                  </span>
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
              <li key={item.question.text}>
                <div className="question-header">
                  <span className="question-index">{index + 1}</span>
                  <p>{item.question.text}</p>
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

      {stage === 'summary' && track && isUnknownTrack && (
        <div className="card assessment-summary-card">
          <div className="assessment-summary-hero">
            <div className="score-ring">
              {topRecommendation ? (
                <>
                  <strong style={{ fontSize: '1.4rem' }}>{topRecommendation.alignment}%</strong>
                  <span>Top match alignment</span>
                </>
              ) : (
                <>
                  <strong>—</strong>
                  <span>No signal yet</span>
                </>
              )}
            </div>
            <div className="summary-copy">
              <p className="section-title">Recommendation</p>
              <h3>{topRecommendation ? `Your strongest signal: ${topRecommendation.title}` : 'Field recommendation'}</h3>
              <p>{scoreStory}</p>
            </div>
          </div>

          <div style={{ marginTop: 24 }}>
            <h4 style={{ marginBottom: 12 }}>Field alignment ranking</h4>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              {unknownRecommendations.map((rec, idx) => (
                <div
                  key={rec.trackId}
                  style={{
                    display: 'grid',
                    gridTemplateColumns: '24px 1fr 60px',
                    alignItems: 'center',
                    gap: 12
                  }}
                >
                  <span style={{ color: '#7c91c1', fontSize: '0.85rem' }}>#{idx + 1}</span>
                  <div>
                    <div style={{ marginBottom: 4 }}>{rec.title}</div>
                    <div
                      style={{
                        height: 8,
                        background: 'rgba(255,255,255,0.06)',
                        borderRadius: 4,
                        overflow: 'hidden'
                      }}
                    >
                      <div
                        style={{
                          width: `${rec.alignment}%`,
                          height: '100%',
                          background: idx === 0
                            ? 'linear-gradient(90deg,#34d399,#4ade80)'
                            : 'linear-gradient(90deg,#6495ff,#a78bfa)'
                        }}
                      />
                    </div>
                  </div>
                  <span style={{ textAlign: 'right', fontSize: '0.9rem', color: '#cdd9ff' }}>
                    {rec.alignment}%
                  </span>
                </div>
              ))}
            </div>
          </div>

          <div className="assessment-summary-actions" style={{ marginTop: 28 }}>
            <button type="button" className="ghost-button" onClick={handleRetake}>
              Retake preferences
            </button>
            {topRecommendation && topRecommendation.alignment > 0 && (
              <button
                type="button"
                className="assessment-primary"
                onClick={() => handleSelectTrack(topRecommendation.trackId)}
              >
                Take the {topRecommendation.title} assessment next →
              </button>
            )}
            <button type="button" className="ghost-button" onClick={handleChooseAnother}>
              Choose another track
            </button>
          </div>
        </div>
      )}

      {stage === 'summary' && track && !isUnknownTrack && (
        <div className="card assessment-summary-card">
          {/* Compact hero — score + maturity + track name only. The score story,
              skill-level paragraph, and focus-area pills were redundant once the
              maturity badge and AI insight took over the "what does this score
              mean" job, so they were removed. */}
          <div className="assessment-summary-hero">
            <div className="score-ring">
              <strong>{scorePercent}%</strong>
              <span>Overall readiness</span>
              {maturity && (
                <div className="maturity-badge" title={maturity.description}>
                  {maturity.label}
                </div>
              )}
            </div>
            <div className="summary-copy">
              <p className="section-title">Track</p>
              <h3 style={{ marginBottom: 0 }}>{track.title}</h3>
            </div>
          </div>

          {/* Personalized AI insight — sits prominently above the score breakdown
              because the user's value-per-pixel is highest here. Hidden on call
              failure (the rest of the summary remains usable). */}
          {(insightLoading || aiInsight) && (
            <div
              style={{
                marginBottom: 20,
                padding: 16,
                borderRadius: 14,
                background: 'linear-gradient(135deg, rgba(34,211,238,0.08), rgba(100,149,255,0.08))',
                border: '1px solid rgba(34, 211, 238, 0.25)'
              }}
            >
              <p
                className="section-title"
                style={{ color: '#22d3ee', marginBottom: 8 }}
              >
                ✨ Personalized insight
              </p>
              {insightLoading && (
                <p className="muted" style={{ margin: 0 }}>Synthesizing your results with your profile…</p>
              )}
              {aiInsight && !insightLoading && (
                <p style={{ margin: 0, color: '#e8eeff', lineHeight: 1.55 }}>{aiInsight}</p>
              )}
            </div>
          )}

          {/* Score breakdown as horizontal bars. Replaces the previous trio of
              equal-size cards so the weakest area visually pops. */}
          <div style={{ marginBottom: 24 }}>
            <h4 style={{ marginBottom: 12 }}>How your score breaks down</h4>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              {breakdown.map(bucket => {
                const isWeakest = bucket.bucket === bucketAnnotations.weakestBucket;
                const isStrongest = bucket.bucket === bucketAnnotations.strongestBucket;
                const score = bucket.score ?? 0;
                const fillGradient = isWeakest
                  ? 'linear-gradient(90deg,#f59e0b,#fb923c)'   // amber for weakest
                  : isStrongest
                    ? 'linear-gradient(90deg,#34d399,#4ade80)' // green for strongest
                    : 'linear-gradient(90deg,#6495ff,#a78bfa)'; // brand for middle
                return (
                  <div
                    key={bucket.bucket}
                    style={{
                      display: 'grid',
                      gridTemplateColumns: 'minmax(180px, 1fr) 2fr 60px',
                      alignItems: 'center',
                      gap: 12
                    }}
                  >
                    <div>
                      <div style={{ color: '#e8eeff', fontWeight: 500 }}>
                        {bucket.label}
                        {isWeakest && (
                          <span style={{ marginLeft: 8, fontSize: '0.7rem', color: '#fb923c' }}>
                            ⚠ weakest
                          </span>
                        )}
                        {isStrongest && (
                          <span style={{ marginLeft: 8, fontSize: '0.7rem', color: '#4ade80' }}>
                            ⭐ strongest
                          </span>
                        )}
                      </div>
                      <small style={{ color: '#7c91c1' }}>{bucket.description}</small>
                    </div>
                    <div
                      style={{
                        height: 10,
                        background: 'rgba(255,255,255,0.06)',
                        borderRadius: 6,
                        overflow: 'hidden'
                      }}
                    >
                      <div
                        style={{
                          width: `${score}%`,
                          height: '100%',
                          background: fillGradient,
                          transition: 'width 0.4s ease'
                        }}
                      />
                    </div>
                    <span style={{ textAlign: 'right', color: '#cdd9ff', fontVariantNumeric: 'tabular-nums' }}>
                      {bucket.score === null ? '—' : `${bucket.score}%`}
                    </span>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Single Next moves list — merges the previous Growth Focus column.
              The "For you:" item was already pulled from the user's lowest-rated
              question's growthHint, so the standalone Growth Focus list was just
              telling the user the same thing twice. */}
          <div style={{ marginBottom: 24 }}>
            <h4 style={{ marginBottom: 12 }}>Next moves</h4>
            <ul style={{ marginTop: 0 }}>
              {personalizedNextMoves.map((step, idx) => (
                <li key={`${idx}-${step}`} style={{ marginBottom: 6 }}>
                  {idx === 0 && personalizedNextMoves.length > track.nextSteps.length ? (
                    <>
                      <strong style={{ color: '#22d3ee' }}>For you:</strong> {step}
                    </>
                  ) : (
                    step
                  )}
                </li>
              ))}
            </ul>
            <p className="muted" style={{ marginBottom: 0 }}>{track.followUp}</p>
            {showResourcesCta && (
              <button
                type="button"
                className="assessment-primary"
                style={{ marginTop: 14, width: '100%' }}
                onClick={() => onJumpToResources!(track.id as NamedTrackId)}
              >
                Browse {resourceCount} {resourceCount === 1 ? 'resource' : 'resources'} for {track.title} →
              </button>
            )}
          </div>

          {/* Where you're strong — kept separate (it's reinforcement, not action),
              but smaller now and without the verbose "Lean into it:" sub-bullets
              that were cluttering the layout. */}
          {strengths.length > 0 && (
            <div style={{ marginBottom: 24 }}>
              <h4 style={{ marginBottom: 8 }}>Where you're strong</h4>
              <ul style={{ marginTop: 0, color: '#cdd9ff' }}>
                {strengths.slice(0, 5).map(item => (
                  <li key={item.text} style={{ marginBottom: 4 }}>{item.text}</li>
                ))}
              </ul>
            </div>
          )}

          {/* Question review collapsed by default — power users can expand,
              everyone else gets a much quieter page. */}
          <details style={{ marginBottom: 16 }}>
            <summary
              style={{
                cursor: 'pointer',
                color: '#8ea5d9',
                fontSize: '0.9rem',
                padding: '6px 0',
                userSelect: 'none'
              }}
            >
              See all {questionRatings.length} question ratings
            </summary>
            <div className="assessment-question-review" style={{ marginTop: 12 }}>
              {questionRatings.map(item => (
                <div key={item.question.text} className="question-review-row">
                  <div>
                    <p>{item.question.text}</p>
                    <small>{item.rating === null ? 'No response recorded' : RATING_LABELS[item.rating]}</small>
                  </div>
                  <span className="badge">{item.rating ?? '—'}/4</span>
                </div>
              ))}
            </div>
          </details>

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
