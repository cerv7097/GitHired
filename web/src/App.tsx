import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import AgentChat from './AgentChat';
import Assessment, { TRACK_ICONS } from './Assessment';
import Jobs from './Jobs';
import ResumeUpload from './ResumeUpload';
import type { User } from './Login';

const API_BASE = import.meta.env.VITE_API_BASE ?? '';

interface RecommendedJob {
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

interface MatchedOn {
  roles: string[];
  skills: string[];
  tools?: string[];
  experienceLevel: string;
  atsScore?: number | null;
  searchQueries?: string[];
  location?: string | null;
}

function loadStoredAtsScore(userId: string) {
  if (typeof window === 'undefined') return null;
  const stored = window.localStorage.getItem(`atsScore:${userId}`);
  const parsed = stored ? Number.parseInt(stored, 10) : NaN;
  if (!Number.isFinite(parsed)) return null;
  return Math.min(100, Math.max(0, parsed));
}

type ActiveTab = 'dashboard' | 'jobs' | 'assessment' | 'resources';

type IndustryId =
  | 'software-development'
  | 'platforms-ops'
  | 'data-ai'
  | 'security'
  | 'product-design'
  | 'customer-solutions'
  | 'enterprise-domain';

interface IndustryOption {
  id: IndustryId;
  label: string;
  focus: string;
  signal: string;
}

interface ResourceItem {
  title: string;
  provider: string;
  format: string;
  level: string;
  duration: string;
  link: string;
  industries: IndustryId[];
  tags: string[];
}

interface ResourceCategory {
  id: string;
  title: string;
  description: string;
  items: ResourceItem[];
}

interface AppProps {
  user: User;
  onLogout: () => void;
  onUserUpdate: (updated: User) => void;
}

export default function App({ user, onLogout, onUserUpdate }: AppProps) {
  const resumeSectionRef = useRef<HTMLDivElement | null>(null);
  const chatSectionRef = useRef<HTMLDivElement | null>(null);
  const [atsScore, setAtsScore] = useState<number | null>(() => loadStoredAtsScore(user.id));
  const [activeTab, setActiveTab] = useState<ActiveTab>('dashboard');
  // Bumped every time the user clicks the "Assessment" item in the top nav. Passed as
  // a `key` to <Assessment />, which forces React to remount it — so clicking the
  // Assessment tab while already viewing the summary sends the user back to the
  // track selection screen, no scrolling required.
  const [assessmentNonce, setAssessmentNonce] = useState(0);
  const [profileMenuOpen, setProfileMenuOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [settingsTab, setSettingsTab] = useState<'password' | 'email' | 'display'>('password');

  // Theme — persisted in localStorage scoped to the user. Applied to <html> via
  // the data-theme attribute, which the CSS variable system in index.css reads
  // to swap colors across all pages.
  const themeStorageKey = `theme:${user.id}`;
  const [theme, setTheme] = useState<'dark' | 'light'>(() => {
    if (typeof window === 'undefined') return 'dark';
    const stored = window.localStorage.getItem(themeStorageKey);
    return stored === 'light' ? 'light' : 'dark';
  });

  useEffect(() => {
    if (typeof document === 'undefined') return;
    if (theme === 'light') {
      document.documentElement.setAttribute('data-theme', 'light');
    } else {
      document.documentElement.removeAttribute('data-theme');
    }
    try {
      window.localStorage.setItem(themeStorageKey, theme);
    } catch {
      // Storage unavailable — preference still applies in this session.
    }
  }, [theme, themeStorageKey]);

  // Change Password state
  const [cpCurrent, setCpCurrent] = useState('');
  const [cpNew, setCpNew] = useState('');
  const [cpConfirm, setCpConfirm] = useState('');
  const [cpLoading, setCpLoading] = useState(false);
  const [cpError, setCpError] = useState<string | null>(null);
  const [cpSuccess, setCpSuccess] = useState<string | null>(null);

  // Change Email state
  const [ceNewEmail, setCeNewEmail] = useState('');
  const [ceCode, setCeCode] = useState('');
  const [ceStep, setCeStep] = useState<'request' | 'confirm'>('request');
  const [ceLoading, setCeLoading] = useState(false);
  const [ceError, setCeError] = useState<string | null>(null);
  const [ceSuccess, setCeSuccess] = useState<string | null>(null);

  function openSettings(tab: 'password' | 'email' | 'display') {
    setSettingsTab(tab);
    setSettingsOpen(true);
    setProfileMenuOpen(false);
    setCpCurrent(''); setCpNew(''); setCpConfirm(''); setCpError(null); setCpSuccess(null);
    setCeNewEmail(''); setCeCode(''); setCeStep('request'); setCeError(null); setCeSuccess(null);
  }

  async function handleChangePassword(e: React.FormEvent) {
    e.preventDefault();
    setCpError(null);
    setCpSuccess(null);
    if (cpNew !== cpConfirm) { setCpError('New passwords do not match.'); return; }
    if (cpNew.length < 6) { setCpError('New password must be at least 6 characters.'); return; }
    setCpLoading(true);
    try {
      const token = localStorage.getItem('cc_token');
      const res = await fetch(`${API_BASE}/api/auth/change-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify({ currentPassword: cpCurrent, newPassword: cpNew }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) { setCpError(data.error ?? 'Failed to update password.'); return; }
      setCpSuccess(data.message ?? 'Password updated successfully.');
      setCpCurrent(''); setCpNew(''); setCpConfirm('');
    } catch {
      setCpError('Could not reach the server.');
    } finally {
      setCpLoading(false);
    }
  }

  async function sendEmailChangeCode() {
    setCeError(null);
    setCeSuccess(null);
    setCeLoading(true);
    try {
      const token = localStorage.getItem('cc_token');
      const res = await fetch(`${API_BASE}/api/auth/request-email-change`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify({ newEmail: ceNewEmail }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) { setCeError(data.error ?? 'Failed to send confirmation code.'); return; }
      setCeSuccess(data.message ?? 'Confirmation code sent to your new email.');
      setCeStep('confirm');
    } catch {
      setCeError('Could not reach the server.');
    } finally {
      setCeLoading(false);
    }
  }

  async function handleRequestEmailChange(e: React.FormEvent) {
    e.preventDefault();
    await sendEmailChangeCode();
  }

  async function handleConfirmEmailChange(e: React.FormEvent) {
    e.preventDefault();
    setCeError(null);
    setCeSuccess(null);
    setCeLoading(true);
    try {
      const token = localStorage.getItem('cc_token');
      const res = await fetch(`${API_BASE}/api/auth/confirm-email-change`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
        body: JSON.stringify({ code: ceCode }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) { setCeError(data.error ?? 'Confirmation failed.'); return; }
      setCeSuccess('Email updated successfully!');
      onUserUpdate({ ...user, email: data.newEmail });
      setCeStep('request');
      setCeNewEmail('');
      setCeCode('');
    } catch {
      setCeError('Could not reach the server.');
    } finally {
      setCeLoading(false);
    }
  }
  const [selectedIndustry, setSelectedIndustry] = useState<IndustryId | 'all'>('all');
  const [resourceSearch, setResourceSearch] = useState('');
  const [skillsModalOpen, setSkillsModalOpen] = useState(false);
  const [recommendations, setRecommendations] = useState<RecommendedJob[]>([]);
  const [recsLoading, setRecsLoading] = useState(false);
  const [matchedOn, setMatchedOn] = useState<MatchedOn | null>(null);
  const [recsTotalResults, setRecsTotalResults] = useState(0);

  const industryOptions: IndustryOption[] = [
    {
      id: 'software-development',
      label: 'Software Development & Applications',
      focus: 'Full-stack delivery, APIs, QA, and shipping cadence.',
      signal: 'Build + ship + iterate'
    },
    {
      id: 'platforms-ops',
      label: 'Platforms & Operations',
      focus: 'Infrastructure, reliability, automation, and enablement.',
      signal: 'Scale + automate + harden'
    },
    {
      id: 'data-ai',
      label: 'Data, AI, & Analytics',
      focus: 'Data modeling, ML, experimentation, and storytelling.',
      signal: 'Measure + model + learn'
    },
    {
      id: 'security',
      label: 'Security',
      focus: 'Threat modeling, AppSec, compliance, and governance.',
      signal: 'Protect + govern + audit'
    },
    {
      id: 'product-design',
      label: 'Product Design & Delivery',
      focus: 'Research, prototyping, design systems, and outcomes.',
      signal: 'Discover + design + launch'
    },
    {
      id: 'customer-solutions',
      label: 'Customer Solutions',
      focus: 'Implementation, success, enablement, and expansion.',
      signal: 'Adopt + retain + grow'
    },
    {
      id: 'enterprise-domain',
      label: 'Enterprise & Domain Tech',
      focus: 'Vertical tech, domain depth, and enterprise system integration.',
      signal: 'Specialize + integrate + comply'
    }
  ];

  const resourceCategories: ResourceCategory[] = [
    {
      id: 'certifications',
      title: 'Certifications',
      description: 'Role-aligned credentials hiring teams recognize.',
      items: [
        {
          title: 'AWS Certified Solutions Architect - Associate',
          provider: 'AWS Training',
          format: 'Exam + prep',
          level: 'Intermediate',
          duration: '6-10 weeks',
          link: 'https://aws.amazon.com/certification/certified-solutions-architect-associate/',
          industries: ['platforms-ops', 'enterprise-domain'],
          tags: ['Cloud', 'Architecture', 'Reliability']
        },
        {
          title: 'Google Professional Data Engineer',
          provider: 'Google Cloud',
          format: 'Exam + labs',
          level: 'Advanced',
          duration: '8-12 weeks',
          link: 'https://cloud.google.com/certification/data-engineer',
          industries: ['data-ai'],
          tags: ['Data', 'Pipelines', 'ML']
        },
        {
          title: 'IBM Data Science Professional Certificate',
          provider: 'Coursera',
          format: 'Exam + labs',
          level: 'Intermediate',
          duration: '3-6 months',
          link: 'https://www.coursera.org/professional-certificates/ibm-data-science',
          industries: ['data-ai'],
          tags: ['Python', 'ML', 'Statistics']
        },
        {
          title: 'AWS Machine Learning – Specialty',
          provider: 'AWS Training',
          format: 'Exam + prep',
          level: 'Advanced',
          duration: '10-14 weeks',
          link: 'https://aws.amazon.com/certification/certified-machine-learning-specialty/',
          industries: ['data-ai'],
          tags: ['ML', 'SageMaker', 'Modeling']
        },
        {
          title: 'Certified Information Systems Security Professional (CISSP)',
          provider: '(ISC)2',
          format: 'Exam',
          level: 'Advanced',
          duration: '10-14 weeks',
          link: 'https://www.isc2.org/certifications/cissp',
          industries: ['security', 'enterprise-domain'],
          tags: ['Governance', 'Risk', 'Compliance']
        },
        {
          title: 'CompTIA Security+',
          provider: 'CompTIA',
          format: 'Exam',
          level: 'Intermediate',
          duration: '6-10 weeks',
          link: 'https://www.comptia.org/certifications/security',
          industries: ['security'],
          tags: ['Network Security', 'Threats', 'Cryptography']
        },
        {
          title: 'Certified Ethical Hacker (CEH)',
          provider: 'EC-Council',
          format: 'Exam + labs',
          level: 'Advanced',
          duration: '8-12 weeks',
          link: 'https://www.eccouncil.org/programs/certified-ethical-hacker-ceh/',
          industries: ['security'],
          tags: ['Pentesting', 'Exploits', 'Red Team']
        },
        {
          title: 'Certified Scrum Product Owner (CSPO)',
          provider: 'Scrum Alliance',
          format: 'Workshop',
          level: 'Intermediate',
          duration: '2-4 days',
          link: 'https://www.scrumalliance.org/get-certified/product-owner-track/certified-scrum-product-owner',
          industries: ['product-design', 'customer-solutions'],
          tags: ['Product', 'Agile', 'Leadership']
        },
        {
          title: 'ITIL 4 Foundation',
          provider: 'AXELOS',
          format: 'Exam',
          level: 'Beginner',
          duration: '4-6 weeks',
          link: 'https://www.axelos.com/',
          industries: ['customer-solutions'],
          tags: ['Service Management', 'ITSM', 'Delivery']
        },
        {
          title: 'Professional Scrum Master I (PSM I)',
          provider: 'Scrum.org',
          format: 'Exam',
          level: 'Intermediate',
          duration: '2-4 weeks',
          link: 'https://www.scrum.org/',
          industries: ['product-design'],
          tags: ['Scrum', 'Agile', 'Facilitation']
        },
        {
          title: 'Project Management Professional (PMP)',
          provider: 'PMI',
          format: 'Exam + prep',
          level: 'Advanced',
          duration: '8-12 weeks',
          link: 'https://www.pmi.org/certifications/project-management-pmp',
          industries: ['product-design', 'customer-solutions'],
          tags: ['PM', 'Delivery', 'Stakeholders']
        },
        {
          title: 'AWS Certified Developer – Associate',
          provider: 'AWS Training',
          format: 'Exam + prep',
          level: 'Intermediate',
          duration: '6-10 weeks',
          link: 'https://aws.amazon.com/certification/certified-developer-associate/',
          industries: ['software-development'],
          tags: ['APIs', 'Lambda', 'DevOps']
        },
        {
          title: 'Oracle Certified Professional: Java SE',
          provider: 'Oracle',
          format: 'Exam',
          level: 'Intermediate',
          duration: '8-12 weeks',
          link: 'https://education.oracle.com/java-se/',
          industries: ['software-development'],
          tags: ['Java', 'OOP', 'Backend']
        },
        {
          title: 'Microsoft Certified: Azure Developer Associate',
          provider: 'Microsoft',
          format: 'Exam + labs',
          level: 'Intermediate',
          duration: '6-10 weeks',
          link: 'https://learn.microsoft.com/certifications/azure-developer/',
          industries: ['software-development'],
          tags: ['Azure', 'APIs', 'Cloud']
        },
        {
          title: 'CompTIA A+',
          provider: 'CompTIA',
          format: 'Exam',
          level: 'Beginner → Intermediate',
          duration: '6-10 weeks',
          link: 'https://www.comptia.org/certifications/a',
          industries: ['platforms-ops'],
          tags: ['Hardware', 'Troubleshooting', 'IT Support']
        },
        {
          title: 'CompTIA Linux+',
          provider: 'CompTIA',
          format: 'Exam',
          level: 'Intermediate',
          duration: '6-10 weeks',
          link: 'https://www.comptia.org/certifications/linux',
          industries: ['platforms-ops'],
          tags: ['Linux', 'CLI', 'Sysadmin']
        },
        {
          title: 'Red Hat Certified System Administrator (RHCSA)',
          provider: 'Red Hat',
          format: 'Exam + labs',
          level: 'Advanced',
          duration: '8-12 weeks',
          link: 'https://www.redhat.com/en/services/certification',
          industries: ['platforms-ops'],
          tags: ['RHEL', 'Linux', 'Sysadmin']
        },
        {
          title: 'AWS Certified Cloud Practitioner',
          provider: 'AWS Training',
          format: 'Exam + prep',
          level: 'Beginner',
          duration: '4-6 weeks',
          link: 'https://aws.amazon.com/certification/certified-cloud-practitioner/',
          industries: ['platforms-ops', 'enterprise-domain'],
          tags: ['Cloud Basics', 'AWS', 'Billing']
        },
        {
          title: 'Microsoft Azure Administrator (AZ-104)',
          provider: 'Microsoft',
          format: 'Exam + labs',
          level: 'Intermediate',
          duration: '6-10 weeks',
          link: 'https://learn.microsoft.com/certifications/azure-administrator/',
          industries: ['platforms-ops'],
          tags: ['Azure', 'Identity', 'Infrastructure']
        },
        {
          title: 'Google Professional Cloud Architect',
          provider: 'Google Cloud',
          format: 'Exam + prep',
          level: 'Advanced',
          duration: '8-12 weeks',
          link: 'https://cloud.google.com/certification/cloud-architect',
          industries: ['platforms-ops', 'enterprise-domain'],
          tags: ['GCP', 'Architecture', 'Scalability']
        },
        {
          title: 'CompTIA Network+',
          provider: 'CompTIA',
          format: 'Exam',
          level: 'Intermediate',
          duration: '6-10 weeks',
          link: 'https://www.comptia.org/certifications/network',
          industries: ['platforms-ops'],
          tags: ['Networking', 'Protocols', 'Troubleshooting']
        },
        {
          title: 'Cisco CCNA',
          provider: 'Cisco',
          format: 'Exam + labs',
          level: 'Intermediate',
          duration: '8-12 weeks',
          link: 'https://www.cisco.com/c/en/us/training-events/training-certifications/certifications/associate/ccna.html',
          industries: ['platforms-ops'],
          tags: ['Routing', 'Switching', 'Networking']
        },
        {
          title: 'LPIC-1 (Linux Professional Institute)',
          provider: 'Linux Professional Institute',
          format: 'Exam',
          level: 'Intermediate',
          duration: '6-10 weeks',
          link: 'https://www.lpi.org/',
          industries: ['platforms-ops'],
          tags: ['Linux', 'Commands', 'System Admin']
        },
        {
          title: 'Salesforce Certified Administrator',
          provider: 'Salesforce',
          format: 'Exam + prep',
          level: 'Intermediate',
          duration: '6-10 weeks',
          link: 'https://trailhead.salesforce.com/credentials/administrator',
          industries: ['enterprise-domain'],
          tags: ['CRM', 'Salesforce', 'Configuration']
        },
        {
          title: 'ServiceNow Certified System Administrator (CSA)',
          provider: 'ServiceNow',
          format: 'Exam + labs',
          level: 'Intermediate',
          duration: '4-8 weeks',
          link: 'https://www.servicenow.com/services/training-and-certification.html',
          industries: ['enterprise-domain'],
          tags: ['ITSM', 'ServiceNow', 'Workflows']
        }
      ]
    },
    {
      id: 'free-courses',
      title: 'Free Courses',
      description: 'No-cost upskilling that maps to skill gaps fast.',
      items: [
        {
          title: 'Full Stack Open',
          provider: 'University of Helsinki',
          format: 'Course',
          level: 'Intermediate',
          duration: '8-12 weeks',
          link: 'https://fullstackopen.com/en/',
          industries: ['software-development'],
          tags: ['React', 'Node', 'API']
        },
        {
          title: 'freeCodeCamp Full Curriculum',
          provider: 'freeCodeCamp',
          format: 'Course',
          level: 'Beginner → Intermediate',
          duration: 'Self-paced',
          link: 'https://www.freecodecamp.org/',
          industries: ['software-development'],
          tags: ['JavaScript', 'React', 'Web']
        },
        {
          title: 'CS50: Introduction to Computer Science',
          provider: 'Harvard University',
          format: 'Course',
          level: 'Beginner',
          duration: '12 weeks',
          link: 'https://cs50.harvard.edu/',
          industries: ['software-development'],
          tags: ['C', 'Python', 'Algorithms']
        },
        {
          title: 'The Odin Project',
          provider: 'The Odin Project',
          format: 'Course',
          level: 'Beginner → Intermediate',
          duration: 'Self-paced',
          link: 'https://www.theodinproject.com/',
          industries: ['software-development'],
          tags: ['HTML', 'CSS', 'JavaScript']
        },
        {
          title: 'Google Data Analytics Professional Certificate',
          provider: 'Coursera',
          format: 'Course',
          level: 'Beginner',
          duration: '8 weeks',
          link: 'https://www.coursera.org/professional-certificates/google-data-analytics',
          industries: ['data-ai', 'enterprise-domain'],
          tags: ['Analytics', 'SQL', 'Dashboards']
        },
        {
          title: 'Python for Everybody',
          provider: 'Coursera',
          format: 'Course',
          level: 'Beginner',
          duration: '3 months',
          link: 'https://www.coursera.org/specializations/python',
          industries: ['data-ai'],
          tags: ['Python', 'Data', 'APIs']
        },
        {
          title: 'freeCodeCamp Data Analysis Certification',
          provider: 'freeCodeCamp',
          format: 'Course',
          level: 'Intermediate',
          duration: 'Self-paced',
          link: 'https://www.freecodecamp.org/',
          industries: ['data-ai'],
          tags: ['Python', 'Pandas', 'NumPy']
        },
        {
          title: 'Site Reliability Engineering (SRE) Fundamentals',
          provider: 'Google Cloud',
          format: 'Course',
          level: 'Intermediate',
          duration: '4-6 weeks',
          link: 'https://cloud.google.com/sre',
          industries: ['platforms-ops'],
          tags: ['SRE', 'SLIs', 'On-call']
        },
        {
          title: 'Google IT Support Professional Certificate',
          provider: 'Coursera',
          format: 'Course',
          level: 'Beginner',
          duration: '6 months',
          link: 'https://www.coursera.org/professional-certificates/google-it-support',
          industries: ['platforms-ops'],
          tags: ['Networking', 'Troubleshooting', 'Support']
        },
        {
          title: 'Cisco Networking Basics',
          provider: 'Cisco Networking Academy',
          format: 'Course',
          level: 'Beginner',
          duration: 'Self-paced',
          link: 'https://www.netacad.com/',
          industries: ['platforms-ops'],
          tags: ['Networking', 'TCP/IP', 'Routing']
        },
        {
          title: 'IBM SkillsBuild IT Support Courses',
          provider: 'IBM SkillsBuild',
          format: 'Course',
          level: 'Beginner → Intermediate',
          duration: 'Self-paced',
          link: 'https://skillsbuild.org/',
          industries: ['platforms-ops'],
          tags: ['IT Support', 'Infrastructure', 'Tools']
        },
        {
          title: 'Cisco Introduction to Cybersecurity',
          provider: 'Cisco Networking Academy',
          format: 'Course',
          level: 'Beginner',
          duration: 'Self-paced',
          link: 'https://www.netacad.com/',
          industries: ['security'],
          tags: ['Threats', 'Network Defense', 'Fundamentals']
        },
        {
          title: 'OWASP Web Security Academy (WebGoat)',
          provider: 'OWASP',
          format: 'Course',
          level: 'Intermediate',
          duration: 'Self-paced',
          link: 'https://owasp.org/www-project-webgoat/',
          industries: ['security'],
          tags: ['AppSec', 'Vulnerabilities', 'Hands-on']
        },
        {
          title: 'TryHackMe',
          provider: 'TryHackMe',
          format: 'Course',
          level: 'Beginner → Intermediate',
          duration: 'Self-paced',
          link: 'https://tryhackme.com/',
          industries: ['security'],
          tags: ['CTF', 'Pentesting', 'Labs']
        },
        {
          title: 'UX Research & Design Fundamentals',
          provider: 'Interaction Design Foundation',
          format: 'Course',
          level: 'Beginner',
          duration: '6 weeks',
          link: 'https://www.interaction-design.org/',
          industries: ['product-design', 'customer-solutions'],
          tags: ['Research', 'Design', 'Prototyping']
        },
        {
          title: 'Google Digital Garage',
          provider: 'Google',
          format: 'Course',
          level: 'Beginner',
          duration: 'Self-paced',
          link: 'https://learndigital.withgoogle.com/digitalgarage',
          industries: ['customer-solutions'],
          tags: ['Digital Skills', 'Marketing', 'Business']
        },
        {
          title: 'Agile & Leadership Courses',
          provider: 'Coursera',
          format: 'Course',
          level: 'Beginner → Intermediate',
          duration: 'Self-paced',
          link: 'https://www.coursera.org/',
          industries: ['product-design'],
          tags: ['Agile', 'Leadership', 'Strategy']
        },
        {
          title: 'PMI Project Management Basics',
          provider: 'PMI',
          format: 'Course',
          level: 'Beginner',
          duration: 'Self-paced',
          link: 'https://www.pmi.org/',
          industries: ['product-design', 'customer-solutions'],
          tags: ['PM Basics', 'Scope', 'Planning']
        },
        {
          title: 'AWS Cloud Practitioner Essentials',
          provider: 'AWS Skill Builder',
          format: 'Course',
          level: 'Beginner',
          duration: 'Self-paced',
          link: 'https://explore.skillbuilder.aws/',
          industries: ['platforms-ops', 'enterprise-domain'],
          tags: ['Cloud', 'AWS', 'Fundamentals']
        },
        {
          title: 'Microsoft Learn – Azure Fundamentals',
          provider: 'Microsoft',
          format: 'Course',
          level: 'Beginner',
          duration: 'Self-paced',
          link: 'https://learn.microsoft.com/',
          industries: ['platforms-ops', 'enterprise-domain'],
          tags: ['Azure', 'Cloud', 'Microsoft']
        },
        {
          title: 'Google Cloud Skills Boost',
          provider: 'Google Cloud',
          format: 'Course',
          level: 'Beginner → Intermediate',
          duration: 'Self-paced',
          link: 'https://cloud.google.com/training',
          industries: ['platforms-ops'],
          tags: ['GCP', 'Labs', 'Cloud']
        },
        {
          title: 'OpenHPI IT Systems Courses',
          provider: 'openHPI',
          format: 'Course',
          level: 'Beginner → Intermediate',
          duration: 'Self-paced',
          link: 'https://open.hpi.de/',
          industries: ['platforms-ops'],
          tags: ['Systems', 'IT', 'Digital']
        },
        {
          title: 'Salesforce Trailhead',
          provider: 'Salesforce',
          format: 'Course',
          level: 'Beginner → Intermediate',
          duration: 'Self-paced',
          link: 'https://trailhead.salesforce.com/',
          industries: ['enterprise-domain'],
          tags: ['CRM', 'Salesforce', 'Enterprise']
        },
        {
          title: 'openSAP',
          provider: 'SAP',
          format: 'Course',
          level: 'Beginner → Intermediate',
          duration: 'Self-paced',
          link: 'https://open.sap.com/',
          industries: ['enterprise-domain'],
          tags: ['SAP', 'ERP', 'Enterprise']
        }
      ]
    },
    {
      id: 'resume-templates',
      title: 'Resume Templates',
      description: 'Modern layouts tuned for ATS and storytelling.',
      items: [
        {
          title: 'ATS-Friendly Resume Templates',
          provider: 'Canva',
          format: 'Templates',
          level: 'All levels',
          duration: '1 hour',
          link: 'https://www.canva.com/resumes/templates/',
          industries: [
            'software-development',
            'platforms-ops',
            'data-ai',
            'security',
            'product-design',
            'customer-solutions',
            'enterprise-domain'
          ],
          tags: ['Design', 'ATS', 'Layout']
        },
        {
          title: 'Tech Resume Templates',
          provider: 'Overleaf',
          format: 'Templates',
          level: 'All levels',
          duration: '1 hour',
          link: 'https://www.overleaf.com/latex/templates/tagged/cv',
          industries: ['software-development', 'platforms-ops', 'data-ai', 'security'],
          tags: ['LaTeX', 'Structure', 'Clarity']
        },
        {
          title: 'Case Study Resume Format',
          provider: 'Figma Community',
          format: 'Template',
          level: 'Mid-Senior',
          duration: '2 hours',
          link: 'https://www.figma.com/community',
          industries: ['product-design', 'customer-solutions'],
          tags: ['Portfolio', 'Story', 'Impact']
        }
      ]
    },
    {
      id: 'career-readiness',
      title: 'Career Readiness',
      description: 'Role-specific study guides and preparation materials.',
      items: [
        {
          title: 'System Design Primer',
          provider: 'GitHub',
          format: 'Playbook',
          level: 'Intermediate',
          duration: 'Ongoing',
          link: 'https://github.com/donnemartin/system-design-primer',
          industries: ['software-development', 'platforms-ops'],
          tags: ['Architecture', 'Scalability', 'Design']
        },
        {
          title: 'Data Science Practice Guide',
          provider: 'Query Practice',
          format: 'Guide',
          level: 'Intermediate',
          duration: '4 weeks',
          link: 'https://www.interviewquery.com/',
          industries: ['data-ai'],
          tags: ['SQL', 'Stats', 'ML']
        },
        {
          title: 'Security Readiness Checklist',
          provider: 'OWASP',
          format: 'Checklist',
          level: 'Intermediate',
          duration: '2 weeks',
          link: 'https://owasp.org/',
          industries: ['security'],
          tags: ['Threats', 'AppSec', 'Controls']
        },
        {
          title: 'Product Design Case Study Guide',
          provider: 'Designlab',
          format: 'Guide',
          level: 'Intermediate',
          duration: '3-4 weeks',
          link: 'https://designlab.com/',
          industries: ['product-design'],
          tags: ['Case Study', 'Narrative', 'Critique']
        }
      ]
    },
    {
      id: 'portfolio-projects',
      title: 'Portfolio & Projects',
      description: 'Hands-on projects to prove impact fast.',
      items: [
        {
          title: 'Build a Production-Ready API',
          provider: 'API Academy',
          format: 'Project',
          level: 'Intermediate',
          duration: '2 weeks',
          link: 'https://www.postman.com/',
          industries: ['software-development'],
          tags: ['API', 'Docs', 'Testing']
        },
        {
          title: 'Analytics Dashboard Starter Kit',
          provider: 'Data Studio',
          format: 'Project',
          level: 'Beginner',
          duration: '1 week',
          link: 'https://lookerstudio.google.com/',
          industries: ['data-ai', 'customer-solutions'],
          tags: ['Dashboards', 'Insights', 'Storytelling']
        },
        {
          title: 'Incident Response Tabletop',
          provider: 'CISA',
          format: 'Simulation',
          level: 'Intermediate',
          duration: '1 week',
          link: 'https://www.cisa.gov/',
          industries: ['security', 'platforms-ops'],
          tags: ['Response', 'Playbooks', 'Coordination']
        },
        {
          title: 'Vertical Market Case Study',
          provider: 'Notion Templates',
          format: 'Template',
          level: 'All levels',
          duration: '2 hours',
          link: 'https://www.notion.so/templates',
          industries: ['enterprise-domain', 'customer-solutions'],
          tags: ['Domain', 'ROI', 'Narrative']
        }
      ]
    }
  ];

  // Filter by both industry and free-text search. The search matches the title,
  // provider, and any tag, lowercased and trimmed; empty search means no text filter.
  const searchTokens = resourceSearch
    .toLowerCase()
    .split(/\s+/)
    .map(t => t.trim())
    .filter(t => t.length > 0);

  const filteredCategories = resourceCategories.map(category => ({
    ...category,
    items: category.items.filter(item => {
      if (selectedIndustry !== 'all' && !item.industries.includes(selectedIndustry)) return false;
      if (searchTokens.length === 0) return true;
      const haystack = [item.title, item.provider, ...item.tags].join(' ').toLowerCase();
      return searchTokens.every(token => haystack.includes(token));
    })
  }));

  const totalResources = filteredCategories.reduce((sum, category) => sum + category.items.length, 0);

  // Pre-compute resource count per industry so the Assessment summary can show
  // "Browse N resources for {track} →" without needing access to the full
  // resource catalog.
  const resourceCountByTrack = resourceCategories.reduce<Partial<Record<IndustryId, number>>>(
    (counts, category) => {
      for (const item of category.items) {
        for (const ind of item.industries) {
          counts[ind] = (counts[ind] ?? 0) + 1;
        }
      }
      return counts;
    },
    {}
  );

  const handleJumpToResources = (trackId: IndustryId) => {
    setSelectedIndustry(trackId);
    setActiveTab('resources');
  };

  const quickActions = [
    { label: 'Upload Resume', description: 'Refresh your ATS scan', accent: 'primary', action: () => scrollToSection(resumeSectionRef) },
    { label: 'Talk to Coach', description: 'Guidance and insights', action: () => scrollToSection(chatSectionRef) },
    { label: 'Skills Courses', description: 'Close your gaps faster' }
  ];

  const navItems = [
    { label: 'Dashboard', key: 'dashboard', tab: 'dashboard' as const },
    { label: 'Jobs', key: 'jobs', tab: 'jobs' as const },
    { label: 'Assessment', key: 'assessment', tab: 'assessment' as const },
    { label: 'Resources', key: 'resources', tab: 'resources' as const }
  ];

  // Merge LLM-extracted skills + tools into one list for the dashboard tile.
  // Backend keeps them structurally separate (skills are concepts, tools are named
  // products), but to a user looking at "Skills Found" both belong. Deduplication
  // is case-insensitive — "Python" and "python" shouldn't both show up.
  const allSkills = (() => {
    if (!matchedOn) return [] as string[];
    const seen = new Set<string>();
    const merged: string[] = [];
    for (const item of [...(matchedOn.skills ?? []), ...(matchedOn.tools ?? [])]) {
      const key = item.trim().toLowerCase();
      if (!key || seen.has(key)) continue;
      seen.add(key);
      merged.push(item.trim());
    }
    return merged;
  })();

  const readinessStats = [
    {
      label: 'Resume Readiness Score',
      value: atsScore !== null ? `${atsScore}%` : '—',
      meta: atsScore === null ? 'Upload resume' : atsScore >= 80 ? 'Strong profile' : atsScore >= 60 ? 'Good progress' : 'Needs improvement'
    },
    {
      label: 'Matched Jobs',
      value: recsLoading ? '…' : recsTotalResults > 0 ? String(recsTotalResults) : '—',
      meta: matchedOn ? 'Based on your resume' : 'Upload resume to match'
    },
    {
      label: 'Skills Found',
      value: matchedOn ? String(allSkills.length) : '—',
      meta: matchedOn ? '' : 'Upload resume',
      // Special-cased in the render path below — when this is true, the tile is
      // clickable and renders a chip preview + "View all" affordance instead of
      // the standard meta text line.
      isSkills: true as const
    }
  ];

  const progress = [
    {
      label: 'Profile',
      value: atsScore !== null ? Math.min(100, atsScore) : 10,
      color: 'linear-gradient(90deg,#3ac1ff,#22d3ee)'
    },
    {
      label: 'Skills',
      value: matchedOn ? Math.min(100, matchedOn.skills.length * 10) : 0,
      color: 'linear-gradient(90deg,#34d399,#4ade80)'
    }
  ];

  const fetchRecommendations = useCallback(() => {
    setRecsLoading(true);
    fetch(`${API_BASE}/api/jobs/recommended?userId=${encodeURIComponent(user.id)}`)
      .then(res => {
        if (res.status === 404) return null;
        if (!res.ok) throw new Error('Failed');
        return res.json();
      })
      .then(data => {
        if (data) {
          setRecommendations(data.jobs ?? []);
          setMatchedOn(data.matchedOn);
          setRecsTotalResults(data.totalResults);
          if (typeof data.matchedOn?.atsScore === 'number') {
            setAtsScore(Math.min(100, Math.max(0, Math.round(data.matchedOn.atsScore))));
          }
        }
      })
      .catch(() => { /* silently show empty state */ })
      .finally(() => setRecsLoading(false));
  }, [user.id]);

  useEffect(() => {
    setRecommendations([]);
    setMatchedOn(null);
    setRecsTotalResults(0);
    setRecsLoading(false);
  }, [user.id]);

  useEffect(() => {
    fetchRecommendations();
  }, [fetchRecommendations]);

  // Scroll to the top of the page on any tab switch. Without this, clicking
  // "View all" on the dashboard (or any sidebar item) leaves the browser at the
  // previous scroll position, which lands the user mid-page on the new view.
  useEffect(() => {
    window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
  }, [activeTab]);

  const handleResumeUploadStart = () => {
    setRecommendations([]);
    setMatchedOn(null);
    setRecsTotalResults(0);
    setAtsScore(null);
    setRecsLoading(true);
  };

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const key = `atsScore:${user.id}`;
    if (atsScore === null) {
      window.localStorage.removeItem(key);
      return;
    }
    window.localStorage.setItem(key, String(atsScore));
  }, [atsScore]);

  const handleAtsScoreUpdate = (score: number | null) => {
    if (score === null) {
      setAtsScore(null);
      return;
    }
    setAtsScore(Math.min(100, Math.max(0, score)));
  };

  function scrollToSection(ref: RefObject<HTMLDivElement | null>) {
    if (ref.current) {
      ref.current.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }

  return (
    <div className="app-shell">
      <header className="top-nav">
        <div className="nav-brand">
          <img src="/Nextwavelogo.png" alt="NextWave Insights" className="nav-logo" />
          <div>
            <h1 className="nav-app-name">GitHired</h1>
            <small className="nav-company">by NextWave Insights</small>
          </div>
        </div>

        <nav className="nav-links">
          {navItems.map(item => {
            const isActive = activeTab === item.tab;
            return (
              <button
                key={item.key}
                type="button"
                className={isActive ? 'active' : ''}
                onClick={() => {
                  // Clicking the Assessment tab always resets the assessment to a
                  // fresh "Choose Track" view, even if the user is already on the
                  // assessment tab viewing the summary.
                  if (item.tab === 'assessment') setAssessmentNonce(n => n + 1);
                  setActiveTab(item.tab);
                }}
              >
                {item.label}
              </button>
            );
          })}
        </nav>

        <div className="nav-profile">
          <span className="nav-pill status-pill">● System Active</span>
          <div style={{ position: 'relative' }}>
            <button
              type="button"
              className={`profile-trigger${profileMenuOpen ? ' open' : ''}`}
              aria-label="Open profile menu"
              onClick={() => setProfileMenuOpen(o => !o)}
            >
              <span className="profile-initials">{user.firstName[0]}{user.lastName[0]}</span>
              <svg className="profile-settings-icon" xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <circle cx="12" cy="12" r="3"/>
                <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>
              </svg>
            </button>
            {profileMenuOpen && (
              <div className="profile-dropdown">
                <div className="profile-dropdown-header">
                  <div style={{ fontWeight: 600 }}>{user.firstName} {user.lastName}</div>
                  <div style={{ fontSize: '0.78rem', color: '#7c91c1', marginTop: 2 }}>{user.email}</div>
                </div>
                <button type="button" onClick={() => openSettings('password')}>Change Password</button>
                <button type="button" onClick={() => openSettings('email')}>Change Email</button>
                <button type="button" onClick={() => openSettings('display')}>
                  Display {theme === 'light' ? '· Light' : '· Dark'}
                </button>
                <div className="profile-dropdown-divider" />
                <button type="button" onClick={onLogout} style={{ color: '#f87171' }}>Sign Out</button>
              </div>
            )}
          </div>
        </div>
      </header>

      {activeTab === 'dashboard' ? (
        <>
          <section className="dashboard-hero">
            <video
              className="hero-bg-video"
              autoPlay
              loop
              muted
              playsInline
              aria-hidden="true"
            >
              <source src="/Video_Generation_Request_Fulfilled.mp4" type="video/mp4" />
            </video>
            <div className="hero-text">
              <div className="status-pill">SYSTEM ACTIVE</div>
              <h2>Welcome back, {user.firstName}</h2>
              <p style={{ color: '#8ea5d9', maxWidth: 420 }}>
                {atsScore === null
                  ? 'Upload your resume to unlock your ATS score and personalized job matches.'
                  : atsScore >= 80
                  ? 'Strong profile! Explore your recommended roles and start applying.'
                  : atsScore >= 60
                  ? 'Good foundation. Refine your resume further to increase your match rate.'
                  : 'Your resume needs work. Review your ATS score and follow the improvement tips.'}
              </p>
            </div>
            <div className="hero-metrics">
              <div className="hero-metric">
                <div className="hero-metric-label">ATS Compatibility</div>
                <div className="hero-score">{atsScore !== null ? `${atsScore}%` : '—'}</div>
                <div style={{ color: '#22d3ee' }}>
                  {atsScore !== null ? 'Scan results ready' : 'Upload a resume to get your ATS score'}
                </div>
              </div>
              <div className="hero-metric">
                <div className="hero-metric-label">Next Action</div>
                <div className="hero-score">{atsScore === null ? 'Resume' : 'Explore'}</div>
                {atsScore === null ? (
                  <button className="ghost-button" onClick={() => scrollToSection(resumeSectionRef)}>
                    Upload Resume
                  </button>
                ) : (
                  <button className="ghost-button" onClick={() => setActiveTab('jobs')}>
                    View Job Matches
                  </button>
                )}
              </div>
            </div>
          </section>

          <div className="dashboard-grid">
            <aside className="stats-grid">
              {readinessStats.map(stat => {
                // The Skills Found tile is special: it's clickable when there are
                // skills, and renders a small chip preview instead of a comma-
                // separated meta line so the user can SEE skills at a glance.
                const isSkillsTile = 'isSkills' in stat && stat.isSkills;
                const previewSkills = isSkillsTile && allSkills.length > 0 ? allSkills.slice(0, 4) : [];
                const hiddenSkillCount = isSkillsTile ? Math.max(0, allSkills.length - previewSkills.length) : 0;
                const isClickable = isSkillsTile && allSkills.length > 0;

                return (
                  <div
                    className={`stat-card ${isClickable ? 'stat-card-clickable' : ''}`}
                    key={stat.label}
                    onClick={isClickable ? () => setSkillsModalOpen(true) : undefined}
                    role={isClickable ? 'button' : undefined}
                    tabIndex={isClickable ? 0 : undefined}
                    onKeyDown={isClickable ? (e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        setSkillsModalOpen(true);
                      }
                    } : undefined}
                  >
                    <div className="stat-label">{stat.label}</div>
                    <div className="stat-value">{stat.value}</div>
                    {isSkillsTile && allSkills.length > 0 ? (
                      <>
                        <div className="stat-skill-chips">
                          {previewSkills.map(skill => (
                            <span key={skill} className="stat-skill-chip">{skill}</span>
                          ))}
                        </div>
                        <div className="stat-skill-action">
                          {hiddenSkillCount > 0 ? `+${hiddenSkillCount} more · View all →` : 'View all →'}
                        </div>
                      </>
                    ) : (
                      <div className="trend-positive">{stat.meta}</div>
                    )}
                  </div>
                );
              })}
            </aside>

            <section>
              <div className="card" ref={resumeSectionRef}>
                <ResumeUpload
                  userId={user.id}
                  onAtsScoreUpdate={handleAtsScoreUpdate}
                  onUploadStart={handleResumeUploadStart}
                  onUploadSuccess={fetchRecommendations}
                />
              </div>

              <div className="card top-matches">
                <div className="resume-card-header" style={{ borderBottom: 'none', padding: 0, marginBottom: 16 }}>
                  <div>
                    <p className="section-title">Top Matches</p>
                    <h3>Recommended Roles</h3>
                    {matchedOn && (
                      <p style={{ fontSize: '0.8rem', color: '#7c91c1', marginTop: 4 }}>
                        Matched on: {matchedOn.roles.slice(0, 2).join(', ')} · {matchedOn.experienceLevel}
                      </p>
                    )}
                  </div>
                  {recommendations.length > 0 && (
                    <button className="ghost-button" onClick={() => setActiveTab('jobs')}>View all</button>
                  )}
                </div>

                {recsLoading && (
                  <div style={{ display: 'flex', alignItems: 'center', gap: 10, color: '#8ea5d9', padding: '12px 0' }}>
                    <div className="spinner" style={{ width: 16, height: 16 }} />
                    <span>Finding matches based on your profile…</span>
                  </div>
                )}

                {!recsLoading && recommendations.length === 0 && (
                  <div style={{ color: '#8ea5d9', paddingTop: 8 }}>
                    <p style={{ marginBottom: 12 }}>Upload your resume to see personalized job matches.</p>
                    <button className="ghost-button" onClick={() => scrollToSection(resumeSectionRef)}>
                      Upload Resume →
                    </button>
                  </div>
                )}

                {!recsLoading && recommendations.length > 0 && (
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                    {recommendations.slice(0, 5).map((job, i) => (
                      <div className="match-card" key={`${job.title}-${job.company}-${i}`}>
                        <div style={{ flex: 1, minWidth: 0 }}>
                          <strong style={{ display: 'block' }}>{job.title}</strong>
                          <p style={{ margin: '4px 0 0', color: '#8ea5d9', fontSize: '0.85rem' }}>
                            {job.company}
                            {job.location ? ` · ${job.location}` : ''}
                            {job.isRemote ? ' · Remote' : ''}
                          </p>
                        </div>
                        <a href={job.applyLink} target="_blank" rel="noreferrer" className="badge" style={{ textDecoration: 'none', flexShrink: 0 }}>
                          Apply →
                        </a>
                      </div>
                    ))}
                    <p style={{ fontSize: '0.75rem', color: '#556080', marginTop: 4 }}>
                      {Math.min(5, recommendations.length)} of {recsTotalResults} matches · AI-selected for your profile
                    </p>
                  </div>
                )}
              </div>
            </section>

            <section className="right-column">
              <div className="card">
                <p className="section-title">Quick Actions</p>
                <div className="quick-actions">
                  {quickActions.map(action => (
                    <button
                      key={action.label}
                      className={`quick-action ${action.accent ?? ''}`}
                      onClick={action.action}
                      type="button"
                    >
                      <span>{action.label}</span>
                      <small>{action.description}</small>
                    </button>
                  ))}
                </div>
              </div>

              <div className="card progress-card">
                <p className="section-title">Progress Track</p>
                <ul>
                  {progress.map(item => (
                    <li key={item.label}>
                      <div className="progress-label">
                        <span>{item.label}</span>
                        <span>{item.value}%</span>
                      </div>
                      <div className="progress-bar">
                        <span
                          className="progress-fill"
                          style={{ width: `${item.value}%`, background: item.color }}
                        />
                      </div>
                    </li>
                  ))}
                </ul>
              </div>

              <div className="card chat-card" ref={chatSectionRef}>
                <div className="resume-card-header" style={{ borderBottom: 'none', padding: 0, marginBottom: 16 }}>
                  <div>
                    <p className="section-title">AI Career Coach</p>
                    <h3>GitHired Assistant</h3>
                  </div>
                </div>
                <AgentChat userId={user.id} />
              </div>
            </section>
          </div>
        </>
      ) : activeTab === 'jobs' ? (
        <Jobs userId={user.id} initialJobs={recommendations} initialLabel={matchedOn ? `AI-recommended for your profile` : undefined} defaultLocation={matchedOn?.location ?? undefined} />
      ) : activeTab === 'assessment' ? (
        <Assessment
          key={assessmentNonce}
          userId={user.id}
          onJumpToResources={handleJumpToResources}
          resourceCountByTrack={resourceCountByTrack}
        />
      ) : (
        <section className="resources-shell">
          <div className="card resources-hero">
            <div>
              <p className="section-title">Resources Hub</p>
              <h2 style={{ marginBottom: 8 }}>
                Targeted materials for sharper outcomes
                <span className="resources-hero-count">{totalResources} {totalResources === 1 ? 'resource' : 'resources'}</span>
              </h2>
              <p style={{ color: '#9db7ff', maxWidth: 620, margin: 0 }}>
                Every resource here maps to the seven tech tracks. Search by keyword or filter by your industry
                to prioritize what sharpens your next move.
              </p>
            </div>
          </div>

          <div className="resources-layout">
            <aside className="resources-sidebar card">
              {/* Search box — runs across title, provider, and tags so users can
                  jump to "AWS" or "Kubernetes" without scanning every category. */}
              <label className="resources-search">
                <span className="resources-search-icon" aria-hidden>🔍</span>
                <input
                  type="text"
                  placeholder="Search resources"
                  value={resourceSearch}
                  onChange={e => setResourceSearch(e.target.value)}
                  aria-label="Search resources"
                />
                {resourceSearch && (
                  <button
                    type="button"
                    className="resources-search-clear"
                    onClick={() => setResourceSearch('')}
                    aria-label="Clear search"
                  >
                    ×
                  </button>
                )}
              </label>

              <p className="section-title" style={{ marginTop: 18 }}>Industry focus</p>
              <div className="filter-chips resources-industry-chips">
                <button
                  type="button"
                  className={`filter-chip resources-industry-chip ${selectedIndustry === 'all' ? 'active' : ''}`}
                  onClick={() => setSelectedIndustry('all')}
                  aria-pressed={selectedIndustry === 'all'}
                >
                  <span className="resources-industry-icon" aria-hidden>✦</span>
                  All industries
                </button>
                {industryOptions.map(option => (
                  <button
                    key={option.id}
                    type="button"
                    className={`filter-chip resources-industry-chip ${selectedIndustry === option.id ? 'active' : ''}`}
                    onClick={() => setSelectedIndustry(option.id)}
                    aria-pressed={selectedIndustry === option.id}
                  >
                    <span className="resources-industry-icon" aria-hidden>{TRACK_ICONS[option.id]}</span>
                    {option.label}
                  </button>
                ))}
              </div>
            </aside>

            <div className="resources-content">
              {totalResources === 0 && (
                <div className="card resources-empty-state">
                  <h3 style={{ marginTop: 0 }}>No resources match your filters</h3>
                  <p className="muted">
                    Try clearing the search box or picking a different industry.
                  </p>
                </div>
              )}
              {filteredCategories.map(category => (
                <div className="card resources-category" key={category.id}>
                  <div className="resources-category-header">
                    <div>
                      <p className="section-title">{category.title}</p>
                      <h3 style={{ margin: 0 }}>{category.description}</h3>
                    </div>
                    <span className="pill subtle">{category.items.length} {category.items.length === 1 ? 'item' : 'items'}</span>
                  </div>
                  {category.items.length === 0 ? (
                    <div className="resources-empty">
                      No resources match the active filters in this category.
                    </div>
                  ) : (
                    <div className="resource-grid">
                      {category.items.map(item => {
                        const visibleTags = item.tags.slice(0, 3);
                        const hiddenTagCount = item.tags.length - visibleTags.length;
                        return (
                          <a
                            className="resource-card"
                            key={`${category.id}-${item.title}`}
                            href={item.link}
                            target="_blank"
                            rel="noreferrer"
                          >
                            <div className="resource-card-pills">
                              <span className="pill">{item.level}</span>
                              <span className="pill subtle">{item.format}</span>
                            </div>
                            <h4 className="resource-card-title">{item.title}</h4>
                            <p className="resource-provider">{item.provider}</p>
                            {visibleTags.length > 0 && (
                              <div className="resource-card-tags">
                                {visibleTags.map(tag => (
                                  <span key={tag} className="pill subtle small">{tag}</span>
                                ))}
                                {hiddenTagCount > 0 && (
                                  <span className="resource-card-tag-more">+{hiddenTagCount} more</span>
                                )}
                              </div>
                            )}
                            <div className="resource-card-footer">
                              <span className="resource-card-duration">{item.duration}</span>
                              <span className="resource-card-open">Open ↗</span>
                            </div>
                          </a>
                        );
                      })}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        </section>
      )}

      {/* Settings Modal */}
      {/* Skills modal — shows the full list of LLM-extracted skills + tools.
          Triggered by clicking the "Skills Found" stat tile on the dashboard. */}
      {skillsModalOpen && (
        <div className="modal-backdrop" onClick={() => setSkillsModalOpen(false)}>
          <div className="modal-card card" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <h3 style={{ margin: 0, fontSize: '1.1rem' }}>
                Skills found in your resume
                <span className="resources-hero-count" style={{ marginLeft: 10 }}>
                  {allSkills.length}
                </span>
              </h3>
              <button
                type="button"
                className="ghost-button modal-close"
                onClick={() => setSkillsModalOpen(false)}
                aria-label="Close"
              >
                ✕
              </button>
            </div>
            <p className="muted" style={{ marginTop: 0, marginBottom: 16, fontSize: '0.9rem' }}>
              Extracted automatically from your latest resume upload. Re-upload to refresh.
            </p>
            {allSkills.length === 0 ? (
              <p className="muted">No skills detected yet — upload a resume to populate this list.</p>
            ) : (
              <div className="skill-cloud">
                {allSkills.map(skill => (
                  <span key={skill} className="pill subtle">{skill}</span>
                ))}
              </div>
            )}
          </div>
        </div>
      )}

      {settingsOpen && (
        <div className="modal-backdrop" onClick={() => setSettingsOpen(false)}>
          <div className="modal-card card" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <h3 style={{ margin: 0, fontSize: '1.1rem' }}>Account Settings</h3>
              <button type="button" className="ghost-button modal-close" onClick={() => setSettingsOpen(false)} aria-label="Close">✕</button>
            </div>

            <div className="modal-tabs">
              <button type="button" className={settingsTab === 'password' ? 'active' : ''} onClick={() => setSettingsTab('password')}>Password</button>
              <button type="button" className={settingsTab === 'email' ? 'active' : ''} onClick={() => setSettingsTab('email')}>Email</button>
              <button type="button" className={settingsTab === 'display' ? 'active' : ''} onClick={() => setSettingsTab('display')}>Display</button>
            </div>

            {settingsTab === 'password' && (
              <form onSubmit={handleChangePassword}>
                {cpError && <div className="alert error">{cpError}</div>}
                {cpSuccess && <div className="alert success">{cpSuccess}</div>}
                <div className="field" style={{ marginBottom: 12 }}>
                  <label>Current Password</label>
                  <input type="password" value={cpCurrent} onChange={e => setCpCurrent(e.target.value)} required autoComplete="current-password" placeholder="Current password" />
                </div>
                <div className="field" style={{ marginBottom: 12 }}>
                  <label>New Password</label>
                  <input type="password" value={cpNew} onChange={e => setCpNew(e.target.value)} required autoComplete="new-password" placeholder="New password" />
                </div>
                <div className="field" style={{ marginBottom: 20 }}>
                  <label>Confirm New Password</label>
                  <input type="password" value={cpConfirm} onChange={e => setCpConfirm(e.target.value)} required autoComplete="new-password" placeholder="Confirm new password" />
                </div>
                <button type="submit" className="primary-action" disabled={cpLoading}>
                  {cpLoading ? 'Updating…' : 'Update Password'}
                </button>
              </form>
            )}

            {settingsTab === 'email' && (
              <>
                <p style={{ color: '#8ea5d9', fontSize: '0.88rem', marginBottom: 16 }}>
                  Current email: <strong style={{ color: '#f4fbff' }}>{user.email}</strong>
                </p>
                {ceError && <div className="alert error">{ceError}</div>}
                {ceSuccess && <div className="alert success">{ceSuccess}</div>}

                {ceStep === 'request' ? (
                  <form onSubmit={handleRequestEmailChange}>
                    <div className="field" style={{ marginBottom: 20 }}>
                      <label>New Email Address</label>
                      <input type="email" value={ceNewEmail} onChange={e => setCeNewEmail(e.target.value)} required autoComplete="email" placeholder="new@example.com" />
                    </div>
                    <button type="submit" className="primary-action" disabled={ceLoading}>
                      {ceLoading ? 'Sending…' : 'Send Confirmation Code'}
                    </button>
                  </form>
                ) : (
                  <form onSubmit={handleConfirmEmailChange}>
                    <p style={{ color: '#8ea5d9', fontSize: '0.88rem', marginBottom: 16 }}>
                      A code was sent to <strong style={{ color: '#f4fbff' }}>{ceNewEmail}</strong>.
                    </p>
                    <div className="field" style={{ marginBottom: 20 }}>
                      <label>Confirmation Code</label>
                      <input type="text" value={ceCode} onChange={e => setCeCode(e.target.value)} required inputMode="numeric" autoComplete="one-time-code" placeholder="123456" />
                    </div>
                    <button type="submit" className="primary-action" disabled={ceLoading}>
                      {ceLoading ? 'Confirming…' : 'Confirm Email Change'}
                    </button>
                    <button type="button" className="secondary-action" disabled={ceLoading} onClick={sendEmailChangeCode} style={{ marginTop: 12, width: '100%' }}>
                      Resend Code
                    </button>
                  </form>
                )}
              </>
            )}

            {settingsTab === 'display' && (
              <>
                <p className="muted" style={{ marginBottom: 16, fontSize: '0.88rem' }}>
                  Choose how GitHired looks across all pages. The setting is saved to this browser.
                </p>
                <div className="display-toggle">
                  <div className="display-toggle-label">
                    <strong>Light theme</strong>
                    <small>{theme === 'light' ? 'On — using a brighter palette across the app.' : 'Off — using the default dark palette.'}</small>
                  </div>
                  <label className="theme-switch" aria-label="Toggle light theme">
                    <input
                      type="checkbox"
                      checked={theme === 'light'}
                      onChange={e => setTheme(e.target.checked ? 'light' : 'dark')}
                    />
                    <span className="theme-switch-slider" />
                  </label>
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {/* Close profile menu on outside click */}
      {profileMenuOpen && (
        <div style={{ position: 'fixed', inset: 0, zIndex: 99 }} onClick={() => setProfileMenuOpen(false)} />
      )}
    </div>
  );
}
